// DocuChat.Application/Services/ChatService.cs
using System.Text.Json;
using Mapster;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using DocuChat.Application.Interfaces.Services;
using DocuChat.Application.Interfaces.Repositories;
using DocuChat.Application.Common;
using DocuChat.Application.DTOs.Chat;
using DocuChat.Domain.Entities;
using DocuChat.Domain.Enums;

namespace DocuChat.Application.Services;

public class ChatService : IChatService
{
    private readonly IUnitOfWork _uow;
    private readonly IVectorSearch _vectorSearch;
    private readonly ILlmService _llm;
    private readonly ICurrentUser _currentUser;
    private readonly IEmbeddingService _embeddingService;
    private readonly IQuestionCacheRepository _cache;
    private readonly ILogger<ChatService> _logger;
    private readonly double _cacheSimilarityThreshold;

    public ChatService(
        IUnitOfWork uow,
        IVectorSearch vectorSearch,
        ILlmService llm,
        ICurrentUser currentUser,
        IEmbeddingService embeddingService,
        IQuestionCacheRepository cache,
        ILogger<ChatService> logger,
        IConfiguration configuration)
    {
        _uow = uow;
        _vectorSearch = vectorSearch;
        _llm = llm;
        _currentUser = currentUser;
        _embeddingService = embeddingService;
        _cache = cache;
        _logger = logger;
        _cacheSimilarityThreshold = configuration.GetValue("Cache:SimilarityThreshold", 0.87);
    }

    public async Task<Result<AskResponseDto>> AskAsync(AskRequest req, CancellationToken ct)
    {
        // ── Session oluştur / getir ──────────────────────────────────────
        ChatSession session;

        if (req.SessionId.HasValue)
        {
            var foundSession = await _uow.Sessions.GetByIdAsync(req.SessionId.Value, ct);
            if (foundSession is null)
                return Result<AskResponseDto>.Failure(
                    Error.NotFound($"Oturum bulunamadı. Id: {req.SessionId.Value}"));
            session = foundSession;

            if (session.UserId != _currentUser.UserId && !_currentUser.IsInRole(Roles.Admin))
                return Result<AskResponseDto>.Failure(
                    Error.Forbidden("Bu oturuma erişiminiz yok."));
        }
        else
        {
            session = new ChatSession
            {
                UserId = _currentUser.UserId,
                Title = req.Question[..Math.Min(60, req.Question.Length)],
            };
            await _uow.Sessions.AddAsync(session, ct);
            await _uow.SaveChangesAsync(ct);
        }

        // ── Kullanıcı mesajını kaydet ────────────────────────────────────
        await _uow.Messages.AddAsync(new ChatMessage
        {
            SessionId = session.Id,
            Role = MessageRole.User,
            Content = req.Question
        }, ct);
        await _uow.SaveChangesAsync(ct);

        // ── Konuşma geçmişini hazırla ────────────────────────────────────
        var history = new List<(string Role, string Content)>();
        var sessionWithMessages = await _uow.Sessions.GetWithMessagesAsync(session.Id, ct);
        if (sessionWithMessages?.Messages?.Any() == true)
        {
            var allMessages = sessionWithMessages.Messages.OrderBy(m => m.CreatedAt).ToList();
            history = allMessages
                .Take(allMessages.Count - 1)
                .TakeLast(16)
                .Where(m => !m.Content.StartsWith("AŞAĞIDAKİ BELGE PARÇALARINI"))
                .Select(m => (m.Role == MessageRole.User ? "user" : "assistant", m.Content))
                .ToList();
        }

        // ── Belge isimlerini çek (clarification için erken gerekli) ────────
        var docNames = await _uow.GetDocumentNamesAsync(ct);
        var docNameStrings = docNames.Select(d => d.FileName).ToList();

        // ── Belirsizlik / yazım hatası kontrolü: her mesajda çalışır ──────────
        bool? earlyIsCacheable = null;
        try
        {
            bool shouldClarify;
            if (history.Count > 0)
            {
                earlyIsCacheable = await _llm.IsCacheableAsync(req.Question, history, ct);
                shouldClarify = !earlyIsCacheable.Value;
            }
            else
            {
                shouldClarify = true;
            }

            if (shouldClarify && !req.SkipClarification)
            {
                var options = await _llm.GenerateClarificationsAsync(req.Question, history, docNameStrings, ct);
                if (options.Count >= 2)
                {
                    _logger.LogInformation("[Clarify] '{Question}' → {Count} seçenek", req.Question, options.Count);
                    return Result<AskResponseDto>.Success(
                        new AskResponseDto(session.Id, string.Empty, [], null, options));
                }
            }
        }
        catch (Exception ex) { _logger.LogWarning(ex, "[Clarify] Atlandı"); }

        // ── Query Rewriting: yalnızca geçmiş varsa çalıştır (zamir/bağlam çözümlemesi için) ──
        var searchQuestion = req.Question;
        if (history.Count > 0)
        {
            try
            {
                var rewritten = await _llm.RewriteQueryAsync(req.Question, history, ct);
                if (rewritten != req.Question)
                {
                    _logger.LogInformation("[QueryRewrite] '{Original}' → '{Rewritten}'", req.Question, rewritten);
                    searchQuestion = rewritten;
                }
            }
            catch (Exception ex) { _logger.LogWarning(ex, "[QueryRewrite] Atlandı"); }
        }

        // ── Semantik cache kontrolü ──────────────────────────────────────
        var questionVector = await _embeddingService.GetEmbeddingAsync(searchQuestion, ct);

        var relevantDocNames = await _llm.DetectRelevantDocumentsAsync(
            searchQuestion, history, docNameStrings, ct);

        List<Guid>? relevantDocIds = null;
        if (relevantDocNames.Any())
        {
            var matchedDocs = docNames.Where(d =>
                relevantDocNames.Any(r =>
                    d.FileName.Equals(r, StringComparison.OrdinalIgnoreCase) ||
                    d.FileName.Contains(r.Split('.')[0], StringComparison.OrdinalIgnoreCase)
                )).ToList();
            if (matchedDocs.Any())
                relevantDocIds = matchedDocs.Select(d => d.Id).ToList();
        }

        // Belge ID'lerini sıralı string olarak cache key'e ekle
        var docIdKey = relevantDocIds != null
            ? string.Join(",", relevantDocIds.OrderBy(x => x))
            : null;

        var cached = await _cache.FindSimilarAsync(
            questionVector, _cacheSimilarityThreshold, docIdKey, ct);
        if (cached != null)
        {
            var validatedAnswer = await _llm.ValidateCachedAnswerAsync(
                searchQuestion, cached.QuestionText, cached.Answer, history, ct);

            if (validatedAnswer != null)
            {
                _logger.LogInformation("[Cache] HIT VALID — '{Question}' cache'den döndürüldü", cached.QuestionText);

                await _cache.IncrementHitAsync(cached.Id, ct);

                await _uow.Messages.AddAsync(new ChatMessage
                {
                    SessionId = session.Id,
                    Role = MessageRole.Assistant,
                    Content = cached.Answer,
                    ImagesJson = cached.ImagesJson
                }, ct);
                await _uow.SaveChangesAsync(ct);

                var cachedChunks = new List<ChunkResult>();
                if (cached.ImagesJson != null)
                    cachedChunks.Add(new ChunkResult(
                        FileName: string.Empty,
                        Content: cached.Answer,
                        ImagePath: cached.ImagesJson));

                var cachedImgs = cached.ImagesJson != null
                    ? (JsonSerializer.Deserialize<List<string>>(cached.ImagesJson) ?? new())
                    : new List<string>();

                return Result<AskResponseDto>.Success(
                    new AskResponseDto(session.Id, cached.Answer, cachedChunks, cachedImgs));
            }

            _logger.LogInformation("[Cache] HIT INVALID — '{Question}' geçersiz, sıfırdan üretiliyor", searchQuestion);
        }

        // Cache miss — WRITE kararı
        // Rewrite varsa rewrite edilmiş soruyu kontrol et; yoksa erken sonucu kullan (gereksiz ikinci çağrıyı önler)
        bool isIndependent = searchQuestion != req.Question
            ? await _llm.IsCacheableAsync(searchQuestion, history, ct)
            : (earlyIsCacheable ?? await _llm.IsCacheableAsync(searchQuestion, history, ct));
        _logger.LogDebug("[Cache] IsCacheable → {Result}", isIndependent);

        // ── HyDE: Varsayımsal belge üret — embedding kalitesini artırır ────
        string? hydeText = null;
        var hydeEligible = true;
        if (hydeEligible)
        {
            try
            {
                hydeText = await _llm.GenerateHypotheticalDocumentAsync(searchQuestion, ct);
                _logger.LogDebug("[HyDE] {Preview}...", hydeText[..Math.Min(100, hydeText.Length)]);
            }
            catch (Exception ex) { _logger.LogWarning(ex, "[HyDE] Atlandı"); }
        }

        // ── Vector search ────────────────────────────────────────────────
        var chunks = (await _vectorSearch.SearchAsync(
            searchQuestion, ct: ct, relevantDocumentIds: relevantDocIds, hydeText: hydeText))
            .ToList();

        if (chunks.Count == 0)
        {
            const string noData = "Sisteme yüklenmiş belgeler arasında bu soruyla ilgili bilgi bulunamadı.";
            await _uow.Messages.AddAsync(new ChatMessage
            {
                SessionId = session.Id,
                Role = MessageRole.Assistant,
                Content = noData
            }, ct);
            await _uow.SaveChangesAsync(ct);
            return Result<AskResponseDto>.Success(new AskResponseDto(session.Id, noData, []));
        }

        // ── LLM Rerank: 2+ chunk varsa LLM ile yeniden sırala ───────────
        if (chunks.Count >= 2)
        {
            try
            {
                var topK = Math.Min(chunks.Count, 8);
                var rankedIndices = await _llm.RerankChunksAsync(
                    searchQuestion, chunks.Select(c => c.Content).ToList(), topK, ct);
                chunks = rankedIndices.Select(i => chunks[i]).ToList();
                _logger.LogInformation("[Rerank] {Count} chunk yeniden sıralandı", chunks.Count);
            }
            catch (Exception ex) { _logger.LogWarning(ex, "[Rerank] Atlandı"); }
        }

        // ── LLM çağrısı ──────────────────────────────────────────────────
        var answer = await _llm.AskAsync(searchQuestion, chunks, history, ct);

        // ── Image path'leri topla — LlmService ile AYNI sırada ──────────
        var seenPaths = new HashSet<string>();
        var allImagePaths = chunks
            .Where(c => c.ImagePath != null)
            .SelectMany(c =>
            {
                try
                {
                    var parsed = JsonSerializer.Deserialize<List<string>>(c.ImagePath!);
                    return parsed ?? new List<string> { c.ImagePath! };
                }
                catch { return new List<string> { c.ImagePath! }; }
            })
            .Where(p => seenPaths.Add(p))
            .ToList();

        var imagesJson = allImagePaths.Count > 0 ? JsonSerializer.Serialize(allImagePaths) : null;

        // ── Asistan mesajını kaydet ──────────────────────────────────────
        await _uow.Messages.AddAsync(new ChatMessage
        {
            SessionId = session.Id,
            Role = MessageRole.Assistant,
            Content = answer,
            ImagesJson = imagesJson,
        }, ct);
        await _uow.SaveChangesAsync(ct);

        // ── Cache'e yaz ──────────────────────────────────────────────────
        if (isIndependent)
        {
            try
            {
                // searchQuestion (normalize/rewrite edilmiş) ile yaz — read ile tutarlı olsun
                await _cache.AddAsync(new QuestionCache
                {
                    QuestionText = searchQuestion,
                    QuestionVector = questionVector,
                    Answer = answer,
                    ImagesJson = imagesJson,
                    DocumentIds = docIdKey,
                    LastHitAt = DateTime.UtcNow,
                }, ct);
                await _uow.SaveChangesAsync(ct);
                _logger.LogInformation("[Cache] WRITE — '{Question}' (belgeler: {DocIds}) cache'e yazıldı",
                    searchQuestion, docIdKey ?? "tümü");

            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Cache] Yazma hatası — '{Question}'", req.Question);
            }
        }
        else
        {
            _logger.LogDebug("[Cache] SKIP — '{Question}' bağlama özgü, cache'e yazılmadı", req.Question);
        }

        return Result<AskResponseDto>.Success(new AskResponseDto(session.Id, answer, chunks,
            allImagePaths.Count > 0 ? allImagePaths : null));
    }

    public async Task<Result<IReadOnlyList<ChatSessionResponseDto>>> GetMySessionsAsync(CancellationToken ct)
    {
        var sessions = await _uow.Sessions.GetByUserIdAsync(_currentUser.UserId, ct);
        return Result<IReadOnlyList<ChatSessionResponseDto>>.Success(
            sessions.Select(s => s.Adapt<ChatSessionResponseDto>()).ToList());
    }

    public async Task<Result<PaginatedResult<ChatSessionResponseDto>>> GetMySessionsPagedAsync(
        int page, int pageSize, CancellationToken ct)
    {
        var paged = await _uow.Sessions.GetByUserIdPagedAsync(_currentUser.UserId, page, pageSize, ct);
        var dtos = paged.Items.Select(s => s.Adapt<ChatSessionResponseDto>()).ToList();
        return Result<PaginatedResult<ChatSessionResponseDto>>.Success(
            new PaginatedResult<ChatSessionResponseDto>(dtos, paged.TotalCount, paged.Page, paged.PageSize));
    }

    public async Task<Result<PaginatedResult<ChatSessionResponseDto>>> GetMySessionsFilteredAsync(
        int page, int pageSize,
        DateTime? dateFrom, DateTime? dateTo,
        string sortBy, bool ascending,
        CancellationToken ct)
    {
        var paged = await _uow.Sessions.GetByUserIdFilteredAsync(
            _currentUser.UserId, page, pageSize, dateFrom, dateTo, sortBy, ascending, ct);
        var dtos = paged.Items.Select(s => s.Adapt<ChatSessionResponseDto>()).ToList();
        return Result<PaginatedResult<ChatSessionResponseDto>>.Success(
            new PaginatedResult<ChatSessionResponseDto>(dtos, paged.TotalCount, paged.Page, paged.PageSize));
    }

    public async Task<Result<IReadOnlyList<ChatMessageResponseDto>>> GetMessagesAsync(
        Guid sessionId, CancellationToken ct)
    {
        var session = await _uow.Sessions.GetWithMessagesAsync(sessionId, ct);
        if (session is null)
            return Result<IReadOnlyList<ChatMessageResponseDto>>.Failure(
                Error.NotFound("Oturum bulunamadı."));

        if (session.UserId != _currentUser.UserId && !_currentUser.IsInRole(Roles.Admin))
            return Result<IReadOnlyList<ChatMessageResponseDto>>.Failure(
                Error.Forbidden("Bu oturuma erişiminiz yok."));

        var dtos = session.Messages
            .OrderBy(m => m.CreatedAt)
            .Select(m => m.Adapt<ChatMessageResponseDto>())
            .ToList();

        return Result<IReadOnlyList<ChatMessageResponseDto>>.Success(dtos);
    }

    public async Task<Result<PaginatedResult<ChatMessageResponseDto>>> GetMessagesPagedAsync(
        Guid sessionId, int page, int pageSize, CancellationToken ct)
    {
        var session = await _uow.Sessions.GetByIdAsync(sessionId, ct);
        if (session is null)
            return Result<PaginatedResult<ChatMessageResponseDto>>.Failure(
                Error.NotFound("Oturum bulunamadı."));

        if (session.UserId != _currentUser.UserId && !_currentUser.IsInRole(Roles.Admin))
            return Result<PaginatedResult<ChatMessageResponseDto>>.Failure(
                Error.Forbidden("Bu oturuma erişiminiz yok."));

        var pagedSession = await _uow.Sessions.GetWithMessagesPagedAsync(sessionId, page, pageSize, ct);
        var totalCount = await _uow.Messages.CountAsync(m => m.SessionId == sessionId, ct);

        var dtos = (pagedSession?.Messages ?? [])
            .Select(m => m.Adapt<ChatMessageResponseDto>())
            .ToList();

        return Result<PaginatedResult<ChatMessageResponseDto>>.Success(
            new PaginatedResult<ChatMessageResponseDto>(dtos, totalCount, page, pageSize));
    }

    public async Task<Result<bool>> RenameSessionAsync(
        Guid sessionId, string title, CancellationToken ct)
    {
        var session = await _uow.Sessions.GetByIdAsync(sessionId, ct);
        if (session is null)
            return Result<bool>.Failure(Error.NotFound("Oturum bulunamadı."));
        if (session.UserId != _currentUser.UserId && !_currentUser.IsInRole(Roles.Admin))
            return Result<bool>.Failure(Error.Forbidden("Bu oturuma erişiminiz yok."));
        session.Title = title[..Math.Min(60, title.Length)];
        await _uow.SaveChangesAsync(ct);
        return Result<bool>.Success(true);
    }

    public async Task<Result<IReadOnlyList<string>>> GetPopularQuestionsAsync(
        int limit, CancellationToken ct)
    {
        var cached = await _cache.GetTopByHitCountAsync(limit, ct);
        if (cached.Count > 0)
            return Result<IReadOnlyList<string>>.Success(cached);

        var recentMessages = await _uow.Messages.FindAsync(m => m.Role == MessageRole.User, ct);
        var popular = recentMessages
            .Select(m => m.Content.Trim())
            .Where(q => q.Length > 10 && q.Length < 200)
            .Where(q => !q.StartsWith("AŞAĞIDAKİ BELGE PARÇALARINI"))
            .GroupBy(q => NormalizeQuestion(q))
            .OrderByDescending(g => g.Count())
            .Take(limit)
            .Select(g => g.OrderBy(q => q.Length).First())
            .ToList();
        return Result<IReadOnlyList<string>>.Success(popular);
    }

    public async Task<Result<bool>> DeleteSessionAsync(Guid sessionId, CancellationToken ct)
    {
        var session = await _uow.Sessions.GetByIdAsync(sessionId, ct);
        if (session is null)
            return Result<bool>.Failure(Error.NotFound("Oturum bulunamadı."));
        if (session.UserId != _currentUser.UserId && !_currentUser.IsInRole(Roles.Admin))
            return Result<bool>.Failure(Error.Forbidden("Bu oturuma erişiminiz yok."));
        _uow.Sessions.Delete(session);
        await _uow.SaveChangesAsync(ct);
        return Result<bool>.Success(true);
    }

    public async Task<Result<int>> DeleteSessionsBatchAsync(
        IEnumerable<Guid> sessionIds, CancellationToken ct)
    {
        var ids = sessionIds.ToHashSet();
        var sessions = await _uow.Sessions.FindAsync(s => ids.Contains(s.Id), ct);

        int deleted = 0;
        foreach (var session in sessions)
        {
            if (session.UserId != _currentUser.UserId && !_currentUser.IsInRole(Roles.Admin)) continue;
            _uow.Sessions.Delete(session);
            deleted++;
        }

        if (deleted > 0)
            await _uow.SaveChangesAsync(ct);
        _logger.LogInformation("[Batch] {Count}/{Total} oturum silindi", deleted, ids.Count);
        return Result<int>.Success(deleted);
    }

    private static string NormalizeQuestion(string question) =>
        new string(question.ToLowerInvariant()
            .Where(c => char.IsLetterOrDigit(c) || char.IsWhiteSpace(c))
            .ToArray()).Trim();
}
