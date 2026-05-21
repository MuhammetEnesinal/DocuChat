using System.Collections.Concurrent;
using System.Text.Json;
using Mapster;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using DocuChat.Application.Interfaces.UseCases;
using DocuChat.Application.Interfaces.Services;
using DocuChat.Application.Interfaces.Repositories;
using DocuChat.Application.Common;
using DocuChat.Application.DTOs.Chat;
using DocuChat.Domain.Entities;
using DocuChat.Domain.Enums;

namespace DocuChat.Application.UseCases;

public class ChatUseCase : IChatUseCase
{
    private readonly IUnitOfWork _uow;
    private readonly IVectorSearch _vectorSearch;
    private readonly ILlmService _llm;
    private readonly ICurrentUser _currentUser;
    private readonly IEmbeddingService _embeddingService;
    private readonly IQuestionCacheRepository _cache;
    private readonly ILogger<ChatUseCase> _logger;
    private readonly double _cacheSimilarityThreshold;

    // Single-flight: aynı (searchQuestion, docIdKey) tuple'ı eşzamanlı sorgulayan iki istek
    // ikinci LLM çağrısını yapmaz — birincinin cache'e yazmasını bekler ve cache'den okur.
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> _singleFlight = new();

    public ChatUseCase(
        IUnitOfWork uow,
        IVectorSearch vectorSearch,
        ILlmService llm,
        ICurrentUser currentUser,
        IEmbeddingService embeddingService,
        IQuestionCacheRepository cache,
        ILogger<ChatUseCase> logger,
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

        // ── Belge isimlerini + özetlerini çek (clarification + 4A için) ────────
        var docNamesWithSummary = await _uow.Documents.GetDocumentNamesAndSummariesAsync(ct);
        var docNames = docNamesWithSummary.Select(d => (d.Id, d.FileName)).ToList();
        var docNameStrings = docNamesWithSummary.Select(d => d.FileName).ToList();
        var docNamesForDetect = docNamesWithSummary.Select(d => (d.FileName, d.Summary)).ToList();

        // ── Belirsizlik kontrolü + sorgu yeniden yazma (paralel) ─────────────
        // IsCacheable ve RewriteQuery birbirinden bağımsız — eşzamanlı başlatılır.
        // Clarify gerekirse rewrite sonucu atılır (clarify yolu azınlık), aksi halde rewrite kazanılır (~500 ms düşüş).
        Task<bool> isCacheableTask = history.Count > 0
            ? _llm.IsCacheableAsync(req.Question, history, ct)
            : Task.FromResult(false);
        Task<string> rewriteTask = history.Count > 0
            ? _llm.RewriteQueryAsync(req.Question, history, ct)
            : Task.FromResult(req.Question);

        bool? earlyIsCacheable = null;
        try
        {
            bool shouldClarify;
            if (history.Count > 0)
            {
                earlyIsCacheable = await isCacheableTask;
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
                    _logger.LogDebug("[Clarify] '{Question}' → {Count} seçenek", req.Question, options.Count);
                    return Result<AskResponseDto>.Success(
                        new AskResponseDto(session.Id, string.Empty, [], null, options));
                }
            }
        }
        catch (Exception ex) { _logger.LogWarning(ex, "[Clarify] Atlandı"); }

        // RewriteQuery sonucunu topla — clarify gerekmediyse kullanılır.
        var searchQuestion = req.Question;
        if (history.Count > 0)
        {
            try
            {
                var rewritten = await rewriteTask;
                if (rewritten != req.Question)
                {
                    _logger.LogDebug("[QueryRewrite] '{Original}' → '{Rewritten}'", req.Question, rewritten);
                    searchQuestion = rewritten;
                }
            }
            catch (Exception ex) { _logger.LogWarning(ex, "[QueryRewrite] Atlandı"); }
        }

        // ── Embedding + ilgili belge tespiti + HyDE (paralel) ────────────
        // Üçü de searchQuestion'a bağlı, birbirinden bağımsız. HyDE optimistik üretilir:
        // cache hit'te sonuç atılır (~500 ms LLM çağrısı israfı), cache miss'te sum yerine
        // max(individual) latency ile kazanılır.
        var embeddingTask = _embeddingService.GetEmbeddingAsync(searchQuestion, ct);
        var detectTask = _llm.DetectRelevantDocumentsAsync(
            searchQuestion, history, docNamesForDetect, ct);
        var hydeTask = SafeHydeAsync(searchQuestion, ct);
        await Task.WhenAll(embeddingTask, detectTask, hydeTask);
        var questionVector = await embeddingTask;
        var relevantDocNames = await detectTask;
        var hydeText = await hydeTask;

        List<Guid>? relevantDocIds = null;
        // LLM "ALL" sentinel'i döndüyse → tüm belgelerde ara (relevantDocIds = null bırak)
        if (relevantDocNames.Contains("__ALL__"))
        {
            _logger.LogDebug("[DocDetect] ALL sentinel — VectorSearch fallback'i devreye girecek");
        }
        else if (relevantDocNames.Any())
        {
            var matchedDocs = docNames.Where(d =>
                relevantDocNames.Any(r =>
                    d.FileName.Equals(r, StringComparison.OrdinalIgnoreCase) ||
                    d.FileName.Contains(r.Split('.')[0], StringComparison.OrdinalIgnoreCase)
                )).ToList();
            if (matchedDocs.Any())
                relevantDocIds = matchedDocs.Select(d => d.Id).ToList();
        }

        // Belge ID'lerini sıralı string olarak cache key'e ekle.
        // Çevre virgülleri (",<id>,<id>,") tutulur ki ClearByDocumentId guard'lı Contains kullanabilsin.
        var docIdKey = relevantDocIds != null
            ? "," + string.Join(",", relevantDocIds.OrderBy(x => x)) + ","
            : null;

        // 1C: İlgili belgelerin güncel ContentHash'lerini çek → cache lookup'ta filtre.
        // Reprocess sonrası hash değişir → eski cache mismatch'le elenir.
        string? docHashKey = null;
        if (relevantDocIds != null && relevantDocIds.Count > 0)
        {
            var hashes = await _uow.Documents.GetDocumentContentHashesAsync(relevantDocIds, ct);
            // ID sırasına göre hash birleştir (docIdKey ile aynı sıra)
            var orderedHashes = hashes
                .OrderBy(h => h.Id)
                .Select(h => h.ContentHash ?? "")
                .ToList();
            docHashKey = "," + string.Join(",", orderedHashes) + ",";
        }

        // Cache lookup + valid hit serve. İki yerde kullanılır: ilk (eager) + single-flight kilit içi (re-check).
        async Task<AskResponseDto?> TryServeCacheHitAsync(string ctxLabel)
        {
            var hit = await _cache.FindSimilarAsync(
                questionVector, _cacheSimilarityThreshold, docIdKey, docHashKey, ct);
            if (hit is null) return null;

            var validated = await _llm.ValidateCachedAnswerAsync(
                searchQuestion, hit.QuestionText, hit.Answer, history, ct);
            if (validated is null)
            {
                _logger.LogDebug("[Cache] HIT INVALID ({Ctx}) — '{Question}'", ctxLabel, searchQuestion);
                return null;
            }

            _logger.LogInformation("[Cache] HIT VALID ({Ctx}) — '{Question}'", ctxLabel, hit.QuestionText);
            await _cache.IncrementHitAsync(hit.Id, ct);

            await _uow.Messages.AddAsync(new ChatMessage
            {
                SessionId = session.Id,
                Role = MessageRole.Assistant,
                Content = hit.Answer,
                ImagesJson = hit.ImagesJson,
            }, ct);
            await _uow.SaveChangesAsync(ct);

            var hitChunks = new List<ChunkResult>();
            if (hit.ImagesJson != null)
                hitChunks.Add(new ChunkResult(string.Empty, hit.Answer, hit.ImagesJson));
            var hitImgs = hit.ImagesJson != null
                ? (JsonSerializer.Deserialize<List<string>>(hit.ImagesJson) ?? new())
                : new List<string>();

            return new AskResponseDto(session.Id, hit.Answer, hitChunks, hitImgs);
        }

        // 1) Eager kontrol — kilit almadan önce.
        var eagerHit = await TryServeCacheHitAsync("eager");
        if (eagerHit is not null)
            return Result<AskResponseDto>.Success(eagerHit);

        // 2) Single-flight: aynı (searchQuestion, docIdKey) için eşzamanlı duplicate iş yapma.
        var sfKey = $"{searchQuestion}|{docIdKey ?? string.Empty}";
        var sfSem = _singleFlight.GetOrAdd(sfKey, _ => new SemaphoreSlim(1, 1));
        await sfSem.WaitAsync(ct);
        try
        {
            // 3) Kilit alındıktan sonra cache'i tekrar kontrol et — bekleyen istekler ilk yazıma erişebilsin.
            var postLockHit = await TryServeCacheHitAsync("post-lock");
            if (postLockHit is not null)
                return Result<AskResponseDto>.Success(postLockHit);

            // Cache miss — WRITE kararı.
            // Rewrite varsa rewrite edilmiş soruyu kontrol et; yoksa erken sonucu kullan.
            bool isIndependent = searchQuestion != req.Question
                ? await _llm.IsCacheableAsync(searchQuestion, history, ct)
                : (earlyIsCacheable ?? await _llm.IsCacheableAsync(searchQuestion, history, ct));
            _logger.LogInformation("[Cache] IsCacheable kararı → {Result} (false ise cache'e yazılmaz)", isIndependent);

            // HyDE yukarıdaki paralel blokta zaten üretildi (varsa). Vector search'e doğrudan veriyoruz.

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

        // ── LLM Rerank: ≥4 chunk VE >1 belge varsa yeniden sırala ────────
        // Tek belge / az chunk durumunda rerank LLM çağrısı genelde sıralamayı değiştirmiyor.
        var distinctDocs = chunks.Select(c => c.FileName).Distinct().Count();
        if (chunks.Count >= 4 && distinctDocs > 1)
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

        // ── 7B: Final cevap kalite denetimi ──────────────────────────────
        // İki kademeli uyarı:
        //   < 0.4 → HARD warning (büyük uyarı banner, cache'e yazma)
        //   0.4-0.65 → SOFT note (dipnot, cache'e yazma)
        //   ≥ 0.65 → sessiz, cache'e yaz
        try
        {
            var quality = await _llm.ValidateAnswerQualityAsync(searchQuestion, chunks, answer, ct);

            if (quality.Score < 0.4)
            {
                _logger.LogWarning("[AnswerQuality] Kritik düşük skor ({Score:F2}) — Issues: {Issues}",
                    quality.Score, string.Join("; ", quality.Issues));

                answer = "💡 Bu cevap belgelerden derlendi — kritik kararlar için kaynağı doğrulamanızı öneririz.\n\n" + answer;
                isIndependent = false;
            }
            else if (quality.Score < 0.65)
            {
                _logger.LogInformation("[AnswerQuality] Orta skor ({Score:F2}) — Issues: {Issues}",
                    quality.Score, string.Join("; ", quality.Issues));

                answer = answer + "\n\n_ℹ️ Bu cevabın bazı detayları belgelerden tam doğrulanamadı; teyit etmek için kaynaklara göz atabilirsiniz._";
                isIndependent = false;
            }
        }
        catch (Exception ex)
        {
            // Fail-open: validation hatası kullanıcıyı engellemez
            _logger.LogWarning(ex, "[AnswerQuality] Denetim atlandı");
        }

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
                    DocumentContentHashes = docHashKey,  // 1C: reprocess'te mismatch tetikleyici
                    // LastHitAt: ilk yazımda null; ilk hit'te IncrementHitAsync doldurur.
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
            _logger.LogInformation("[Cache] SKIP — '{Question}' cache'e yazılmadı (IsCacheable=false veya AnswerQuality düşük)", req.Question);
        }

            return Result<AskResponseDto>.Success(new AskResponseDto(session.Id, answer, chunks,
                allImagePaths.Count > 0 ? allImagePaths : null));
        }
        finally
        {
            sfSem.Release();
            // Bekleyen kimse yoksa dict'i temizle — sonsuza dek büyümeyi engeller.
            if (sfSem.CurrentCount == 1)
                _singleFlight.TryRemove(sfKey, out _);
        }
    }

    /// <summary>
    /// HyDE çağrısını exception-safe sarar — paralel WhenAll bloğunda kullanılır.
    /// Hata olursa null döner, vector search HyDE'sız çalışmaya devam eder.
    /// </summary>
    private async Task<string?> SafeHydeAsync(string searchQuestion, CancellationToken ct)
    {
        try
        {
            var hyde = await _llm.GenerateHypotheticalDocumentAsync(searchQuestion, ct);
            _logger.LogDebug("[HyDE] {Preview}...", hyde[..Math.Min(100, hyde.Length)]);
            return hyde;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[HyDE] Atlandı");
            return null;
        }
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
        var totalCount = await _uow.Messages.CountBySessionAsync(sessionId, ct);

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

        var recentMessages = await _uow.Messages.GetByRoleAsync(MessageRole.User, ct);
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
        var sessions = await _uow.Sessions.GetByIdsAsync(ids, ct);

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

