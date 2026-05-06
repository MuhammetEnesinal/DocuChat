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
                .TakeLast(6)
                .Where(m => !m.Content.StartsWith("AŞAĞIDAKİ BELGE PARÇALARINI"))
                .Select(m => (m.Role == MessageRole.User ? "user" : "assistant", m.Content))
                .ToList();
        }

        // ── Query Rewriting: kısaltma, yazım hatası, belirsiz zamir temizle ──
        var searchQuestion = req.Question;
        try
        {
            var rewritten = await _llm.RewriteQueryAsync(req.Question, history, ct);
            if (rewritten != req.Question)
            {
                Console.WriteLine($"[QueryRewrite] '{req.Question}' → '{rewritten}'");
                searchQuestion = rewritten;
            }
        }
        catch (Exception ex) { Console.WriteLine($"[QueryRewrite] Atlandı: {ex.Message}"); }

        // ── Semantik cache kontrolü ──────────────────────────────────────
        var questionVector = await _embeddingService.GetEmbeddingAsync(searchQuestion, ct);

        // ── Belge tespiti (cache kontrolünden önce — belge bazlı cache için gerekli) ──
        var docNames = await _uow.GetDocumentNamesAsync(ct);
        var docNameStrings = docNames.Select(d => d.FileName).ToList();

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

        // Cache kontrolü — her zaman kontrol et; isIndependent sadece WRITE kararını etkiler
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

        // Cache miss — WRITE kararı için bağımsızlık kontrolü (rewritten query ile değerlendir)
        // "2. satırı getir", "devam et", "bunu açıkla" → false  |  "Baret nedir?" → true
        var isIndependent = await _llm.IsCacheableAsync(searchQuestion, ct);

        // ── HyDE: Varsayımsal belge üret — embedding kalitesini artırır ────
        string? hydeText = null;
        if (!history.Any())
        {
            try
            {
                hydeText = await _llm.GenerateHypotheticalDocumentAsync(searchQuestion, ct);
                Console.WriteLine($"[HyDE] {hydeText[..Math.Min(100, hydeText.Length)]}...");
            }
            catch (Exception ex) { Console.WriteLine($"[HyDE] Atlandı: {ex.Message}"); }
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

        // ── LLM Rerank: 4+ chunk varsa LLM ile yeniden sırala ───────────
        if (chunks.Count >= 4)
        {
            try
            {
                var topK = Math.Min(5, chunks.Count);
                var rankedIndices = await _llm.RerankChunksAsync(
                    searchQuestion, chunks.Select(c => c.Content).ToList(), topK, ct);
                chunks = rankedIndices.Select(i => chunks[i]).ToList();
                Console.WriteLine($"[Rerank] {chunks.Count} chunk yeniden sıralandı.");
            }
            catch (Exception ex) { Console.WriteLine($"[Rerank] Atlandı: {ex.Message}"); }
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

        // allImagePaths'i aynen koru — [IMG:N] metnindeki N, 1-tabanlı index olarak
        // allImagePaths[N-1]'e doğrudan map edilmeli; filtrelenmiş liste index kaymasına neden olur.
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
        // isIndependent zaten yukarıda hesaplandı — IsCacheableAsync'i tekrar çağırmaya gerek yok
        if (isIndependent)
        {
            try
            {
                await _cache.AddAsync(new QuestionCache
                {
                    QuestionText = req.Question,
                    QuestionVector = questionVector,
                    Answer = answer,
                    ImagesJson = imagesJson,
                    DocumentIds = docIdKey
                }, ct);
                await _uow.SaveChangesAsync(ct);
                Console.WriteLine($"[Cache] WRITE — '{req.Question}' (belgeler: {docIdKey ?? "tümü"}) cache'e yazıldı.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Cache] Yazma hatası: {ex.Message}");
                if (ex.InnerException != null)
                    Console.WriteLine($"[Cache] Inner: {ex.InnerException.Message}");
            }
        }
        else
        {
            Console.WriteLine($"[Cache] SKIP — '{req.Question}' bağlama özgü, cache'e yazılmadı.");
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