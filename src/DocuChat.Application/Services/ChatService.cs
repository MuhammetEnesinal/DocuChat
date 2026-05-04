// DocuChat.Application/Services/ChatService.cs
using System.Text.Json;
using Mapster;
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

    private const double CacheSimilarityThreshold = 0.92;

    public ChatService(
        IUnitOfWork uow,
        IVectorSearch vectorSearch,
        ILlmService llm,
        ICurrentUser currentUser,
        IEmbeddingService embeddingService,
        IQuestionCacheRepository cache)
    {
        _uow = uow;
        _vectorSearch = vectorSearch;
        _llm = llm;
        _currentUser = currentUser;
        _embeddingService = embeddingService;
        _cache = cache;
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
                .TakeLast(10)
                .Where(m => !m.Content.StartsWith("AŞAĞIDAKİ BELGE PARÇALARINI"))
                .Select(m => (m.Role == MessageRole.User ? "user" : "assistant", m.Content))
                .ToList();
        }

        // ── Semantik cache kontrolü ──────────────────────────────────────
        var questionVector = await _embeddingService.GetEmbeddingAsync(req.Question, ct);

        // ── Belge tespiti (cache kontrolünden önce — belge bazlı cache için gerekli) ──
        var docNames = await _uow.GetDocumentNamesAsync(ct);
        var docNameStrings = docNames.Select(d => d.FileName).ToList();

        var relevantDocNames = await _llm.DetectRelevantDocumentsAsync(
            req.Question, history, docNameStrings, ct);

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

        // Cache kontrolü — sadece history yoksa (follow-up sorular cache'e girmemeli)
        if (!history.Any())
        {
            var cached = await _cache.FindSimilarAsync(questionVector, CacheSimilarityThreshold, docIdKey, ct);
            if (cached != null)
            {
                Console.WriteLine($"[Cache] HIT — '{cached.QuestionText}' → cache'den döndürüldü.");

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
        }

        // ── Vector search ────────────────────────────────────────────────
        var chunks = await _vectorSearch.SearchAsync(
            req.Question, ct: ct, relevantDocumentIds: relevantDocIds);

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

        // ── LLM çağrısı ──────────────────────────────────────────────────
        var answer = await _llm.AskAsync(req.Question, chunks, history, ct);

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

        // LLM cevabındaki [IMG:N] işaretlerini parse et
        // Sadece cevabında kullanılan resimleri döndür
        var usedNums = System.Text.RegularExpressions.Regex
            .Matches(answer, @"\[IMG:(\d+)\]")
            .Select(m => int.Parse(m.Groups[1].Value) - 1) // 0-indexed
            .Distinct()
            .OrderBy(n => n)
            .ToList();

        var imagePaths = usedNums.Any()
            ? usedNums.Where(i => i >= 0 && i < allImagePaths.Count).Select(i => allImagePaths[i]).ToList()
            : allImagePaths; // [IMG:N] yoksa tümünü döndür (PDF gibi)

        var imagesJson = imagePaths.Count > 0 ? JsonSerializer.Serialize(imagePaths) : null;


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
        // Koşullar: 1) Yeni sohbet (history yok)
        //           2) LLM sorunun bağımsız olduğunu onayladı
        if (!history.Any())
        {
            try
            {
                // LLM'e sor: bu soru bağımsız mı cache'lenebilir mi?
                var cacheCheckPrompt =
                    $"Bu soru önceki bir konuşma bağlamı olmadan tek başına anlamlı ve cevaplanabilir mi?\n" +
                    $"Soru: \"{req.Question}\"\n" +
                    $"Sadece EVET veya HAYIR yaz.";

                var cacheDecision = await _llm.DetectRelevantDocumentsAsync(
                    cacheCheckPrompt, Enumerable.Empty<(string, string)>(),
                    new[] { "EVET", "HAYIR" }, ct);

                var isCacheable = cacheDecision.Any(d =>
                    d.Equals("EVET", StringComparison.OrdinalIgnoreCase));

                if (isCacheable)
                {
                    await _cache.AddAsync(new QuestionCache
                    {
                        QuestionText = req.Question,
                        QuestionVector = questionVector,
                        Answer = answer,
                        ImagesJson = imagesJson,
                        DocumentIds = docIdKey
                    }, ct);
                    Console.WriteLine($"[Cache] WRITE — '{req.Question}' (belgeler: {docIdKey ?? "tümü"}) cache'e yazıldı.");
                }
                else
                {
                    Console.WriteLine($"[Cache] SKIP — '{req.Question}' bağıma özgü, cache'e yazılmadı.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Cache] Yazma hatası: {ex.Message}");
                if (ex.InnerException != null)
                    Console.WriteLine($"[Cache] Inner: {ex.InnerException.Message}");
            }
        }

        return Result<AskResponseDto>.Success(new AskResponseDto(session.Id, answer, chunks, imagePaths));
    }

    public async Task<Result<IReadOnlyList<ChatSessionResponseDto>>> GetMySessionsAsync(CancellationToken ct)
    {
        var sessions = await _uow.Sessions.GetByUserIdAsync(_currentUser.UserId, ct);
        return Result<IReadOnlyList<ChatSessionResponseDto>>.Success(
            sessions.Select(s => s.Adapt<ChatSessionResponseDto>()).ToList());
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
        var allMessages = await _uow.Messages.FindAsync(m => m.Role == MessageRole.User, ct);
        var popular = allMessages
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

    private static string NormalizeQuestion(string question) =>
        new string(question.ToLowerInvariant()
            .Where(c => char.IsLetterOrDigit(c) || char.IsWhiteSpace(c))
            .ToArray()).Trim();
}