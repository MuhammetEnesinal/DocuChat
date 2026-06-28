using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Mapster;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using DocuChat.Application.Interfaces.UseCases;
using DocuChat.Application.Interfaces.Services;
using DocuChat.Application.Interfaces.Repositories;
using DocuChat.Application.Common;
using DocuChat.Application.Common.Specifications;
using DocuChat.Application.DTOs.Chat;
using DocuChat.Domain.Entities;
using DocuChat.Domain.Enums;
using DocuChat.Application.ServiceContracts;

namespace DocuChat.Application.UseCases;

public class ChatUseCase : IChatUseCase
{
    private readonly IUnitOfWork _uow;
    private readonly IRetrievalPipeline _retrieval;
    private readonly ILlmService _llm;
    private readonly ICurrentUser _currentUser;
    private readonly IEmbeddingService _embeddingService;
    private readonly ITokenCounter _tokenCounter;
    private readonly ILogger<ChatUseCase> _logger;
    private readonly double _cacheSimilarityThreshold;
    private readonly double _cacheHighConfidenceThreshold;
    private readonly int _historyTokenBudget;
    private readonly int _historyMaxMessages;
    private readonly bool _followUpsEnabled;

    public ChatUseCase(
        IUnitOfWork uow,
        IRetrievalPipeline retrieval,
        ILlmService llm,
        ICurrentUser currentUser,
        IEmbeddingService embeddingService,
        ITokenCounter tokenCounter,
        ILogger<ChatUseCase> logger,
        IConfiguration configuration)
    {
        _uow = uow;
        _retrieval = retrieval;
        _llm = llm;
        _currentUser = currentUser;
        _embeddingService = embeddingService;
        _tokenCounter = tokenCounter;
        _logger = logger;
        _cacheSimilarityThreshold = configuration.GetValue("Cache:SimilarityThreshold", 0.87);
        _cacheHighConfidenceThreshold = configuration.GetValue("Cache:HighConfidenceThreshold", 0.95);
        _historyTokenBudget = configuration.GetValue("Chat:HistoryTokenBudget", 3000);
        _historyMaxMessages = configuration.GetValue("Chat:HistoryMaxMessages", 20);
        _followUpsEnabled = configuration.GetValue("Chat:FollowUpsEnabled", true);
    }


    private async Task<List<string>> SafeFollowUpsAsync(
        string question, string answer, IReadOnlyList<ChunkResult> chunks, CancellationToken ct)
    {
        if (!_followUpsEnabled) return new List<string>();
        try
        {
            return await _llm.GenerateFollowUpQuestionsAsync(question, answer, chunks, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[FollowUp] Atlandı");
            return new List<string>();
        }
    }

    // Wrapper sadece kütüphane exception'ı için fallback; LLM'in kendi fail-open'ı zaten Unvalidated.
    private async Task<AnswerQualityResult> SafeValidateAsync(
        string question, IReadOnlyList<ChunkResult> chunks, string answer, CancellationToken ct)
    {
        try
        {
            return await _llm.ValidateAnswerQualityAsync(question, chunks, answer, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[AnswerQuality] Denetim hatası — Unvalidated (cache yazılmaz)");
            return AnswerQualityResult.Unvalidated();
        }
    }

    // ========== STREAMING ASK ==========
    // AskAsync ile aynı pipeline; tek farkı LLM cevabını token-by-token yield etmesi.
    // Self-correct retry streaming yolunda yapılmaz (cevap zaten kullanıcıya akıyor); kalite
    // skoru düşükse sadece cache yazılmaz, kullanıcı badge görür.
    public async IAsyncEnumerable<object> AskStreamAsync(
        AskRequestDto req,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        // === 1. Session resolve / create ===
        ChatSession session;
        if (req.SessionId.HasValue)
        {
            var foundSession = await _uow.Sessions.GetByIdAsync(req.SessionId.Value, ct);
            if (foundSession is null)
            {
                yield return new { type = "error", message = $"Oturum bulunamadı. Id: {req.SessionId.Value}" };
                yield return new { type = "done" };
                yield break;
            }
            session = foundSession;
            if (session.UserId != _currentUser.UserId && !_currentUser.IsInRole(Roles.Admin))
            {
                yield return new { type = "error", message = "Bu oturuma erişiminiz yok." };
                yield return new { type = "done" };
                yield break;
            }
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

        yield return new { type = "start", sessionId = session.Id };

        // === 2. History load (sliding window + conversation summary) ===
        // Son N mesajı ham tut, eski mesajları LLM ile özetle → "system" rolünde özet ekle.
        // Anahtar bağlam (kullanıcı rolü, konu, terimler) bütçe dolduğunda da korunur.
        const int KeepRawCount = 6;  // son 6 mesaj (3 turn) ham
        var history = new List<(string Role, string Content)>();
        var sessionWithMessages = await _uow.Sessions.GetWithMessagesAsync(session.Id, ct);
        if (sessionWithMessages?.Messages?.Any() == true)
        {
            var allMessages = sessionWithMessages.Messages
                .OrderBy(m => m.CreatedAt)
                .Where(m => !m.Content.StartsWith("AŞAĞIDAKİ BELGE PARÇALARINI"))
                .ToList();

            // Son KeepRawCount mesaj → ham
            var rawCount = Math.Min(KeepRawCount, allMessages.Count);
            var rawMessages = allMessages.Skip(allMessages.Count - rawCount).ToList();

            // Önceki mesajlar → özetle (varsa)
            var olderMessages = allMessages.Take(allMessages.Count - rawCount).ToList();
            if (olderMessages.Count > 0)
            {
                try
                {
                    var olderTuples = olderMessages
                        .Select(m => (Role: m.Role == MessageRole.User ? "user" : "assistant", m.Content))
                        .ToList();
                    var summary = await _llm.SummarizeConversationAsync(olderTuples, ct);
                    if (!string.IsNullOrWhiteSpace(summary))
                        history.Add(("system", $"[Önceki konuşma özeti]: {summary}"));
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[History] Özetleme atlandı — sadece son {N} mesaj kullanılacak", rawCount);
                }
            }

            // Son ham mesajlar — token budget kontrolü (özet ile birlikte)
            var budget = _historyTokenBudget;
            if (history.Count > 0) budget -= _tokenCounter.Count(history[0].Content);

            var picked = new List<(string Role, string Content)>();
            for (var i = rawMessages.Count - 1; i >= 0 && picked.Count < _historyMaxMessages; i--)
            {
                var m = rawMessages[i];
                var cost = _tokenCounter.Count(m.Content);
                if (picked.Count > 0 && cost > budget) break;
                budget -= cost;
                picked.Add((m.Role == MessageRole.User ? "user" : "assistant", m.Content));
            }
            picked.Reverse();
            history.AddRange(picked);
        }

        var searchQuestion = req.Question;

        // === 3. Embedding ===
        var questionVector = await _embeddingService.GetEmbeddingAsync(searchQuestion, ct);

        // === 4. Cache lookup (eager) ===
        var cacheMatch = await _uow.QuestionCache.FindSimilarAsync(questionVector, _cacheSimilarityThreshold, ct);
        if (cacheMatch is not null)
        {
            var hit = cacheMatch.Cache;
            string? validated;
            if (cacheMatch.Similarity >= _cacheHighConfidenceThreshold)
            {
                _logger.LogInformation("[Cache][Stream] HIT FAST sim={Sim:F3}", cacheMatch.Similarity);
                validated = hit.Answer;
            }
            else
            {
                validated = await _llm.ValidateCachedAnswerAsync(
                    searchQuestion, hit.QuestionText, hit.Answer, history, ct);
            }

            if (validated is not null)
            {
                var hitChunks = new List<ChunkResult>();
                if (hit.ImagesJson != null)
                    hitChunks.Add(new ChunkResult(string.Empty, hit.Answer, hit.ImagesJson));
                var hitImgs = hit.ImagesJson != null
                    ? (JsonSerializer.Deserialize<List<string>>(hit.ImagesJson) ?? new())
                    : new List<string>();
                var followUpTask = SafeFollowUpsAsync(searchQuestion, hit.Answer, hitChunks, ct);

                await _uow.QuestionCache.IncrementHitAsync(hit.Id, ct);
                var userMsgCache = new ChatMessage
                {
                    SessionId = session.Id,
                    Role = MessageRole.User,
                    Content = searchQuestion
                };
                await _uow.Messages.AddAsync(userMsgCache, ct);
                var assistantMsgCache = new ChatMessage
                {
                    SessionId = session.Id,
                    Role = MessageRole.Assistant,
                    Content = hit.Answer,
                    ImagesJson = hit.ImagesJson,
                    ResponseToMessageId = userMsgCache.Id,
                };
                await _uow.Messages.AddAsync(assistantMsgCache, ct);
                await _uow.SaveChangesAsync(ct);

                var hitFollowUps = await followUpTask;
                yield return new
                {
                    type = "cache_hit",
                    messageId = assistantMsgCache.Id,  // ⭐ feedback için gerçek Guid
                    answer = hit.Answer,
                    images = hitImgs.Count > 0 ? hitImgs : null,
                    followUps = hitFollowUps.Count > 0 ? hitFollowUps : null
                };
                yield return new { type = "done" };
                yield break;
            }
        }

        // === 5. Cache miss: IsCacheable + clarification ===
        var docNamesWithSummary = await _uow.Documents.GetDocumentNamesAndSummariesAsync(ct);
        var docNameStrings = docNamesWithSummary.Select(d => d.FileName).ToList();

        bool isCacheable = history.Count == 0
            || await _llm.IsCacheableAsync(req.Question, history, ct);
        bool shouldClarify = history.Count == 0 || !isCacheable;

        if (shouldClarify && !req.SkipClarification)
        {
            List<string> options;
            try { options = await _llm.GenerateClarificationsAsync(req.Question, history, docNameStrings, ct); }
            catch (Exception ex) { _logger.LogWarning(ex, "[Clarify][Stream] Atlandı"); options = new List<string>(); }

            if (options.Count >= 1)
            {
                yield return new { type = "clarification", options };
                yield return new { type = "done" };
                yield break;
            }
        }

        // Clarification yok → kullanıcı mesajını kaydet
        // Asistan mesajını sonra ResponseToMessageId = userMsg.Id ile linkleyeceğiz.
        var userMsg = new ChatMessage
        {
            SessionId = session.Id,
            Role = MessageRole.User,
            Content = req.Question
        };
        await _uow.Messages.AddAsync(userMsg, ct);
        await _uow.SaveChangesAsync(ct);

        // === 6. Retrieval pipeline ===
        _logger.LogInformation("[Cache][Stream] IsCacheable kararı → {Result}", isCacheable);
        var chunks = (await _retrieval.SearchAsync(
            searchQuestion, history,
            isStandalone: isCacheable,
            ct: ct)).ToList();

        // === 6.5 Personal Question-Similarity Feedback Check ===
        // Mantık:
        //   1. Kullanıcının eski feedback'lerinden SORU BENZERLİĞİ ile match
        //   2. Cluster: birbirine benzeyen feedback'leri grupla (similarity > 0.85)
        //   3. Net: dislike_count - like_count (like aktif iptal)
        //   4. Net > 0 olan cluster'ları al → en yüksek net önce → Take 10
        const double SimilarityThreshold = 0.75;
        const double ClusterThreshold = 0.85;
        const int MaxCandidates = 30;  // DB'den çekilecek max
        const int MaxWarnings = 10;    // LLM'e gidecek max

        string? feedbackContext = null;
        try
        {
            // Yeni sorgunun embedding'i — zaten Aşama 3'te üretildi (questionVector)
            var candidates = await _uow.Feedback.GetSimilarFeedbacksAsync(
                _currentUser.UserId, questionVector, SimilarityThreshold,
                maxAgeMonths: 6, MaxCandidates, ct);

            if (candidates.Count > 0)
            {
                // C# tarafında clustering — birbirine benzeyen feedback'leri grupla
                var clusters = new List<List<ChatMessageFeedback>>();
                foreach (var fb in candidates)
                {
                    var matchedCluster = clusters.FirstOrDefault(cl =>
                        CosineSimilarity(cl[0].QuestionVector, fb.QuestionVector) > ClusterThreshold);
                    if (matchedCluster != null) matchedCluster.Add(fb);
                    else clusters.Add(new List<ChatMessageFeedback> { fb });
                }

                // Her cluster için net = dislike - like
                var warnings = clusters
                    .Select(cl => new
                    {
                        Cluster = cl,
                        Net = cl.Count(f => f.Rating == -1) - cl.Count(f => f.Rating == 1),
                    })
                    .Where(x => x.Net > 0)  // Like'lar dislike'ları geçmediyse dahil et
                    .OrderByDescending(x => x.Net)  // En çok şikayet edilen önce
                    .Take(MaxWarnings)
                    .ToList();

                if (warnings.Count > 0)
                {
                    // Her cluster için en yeni dislike'ı "temsilci" olarak al
                    var items = warnings
                        .Select(w =>
                        {
                            var rep = w.Cluster
                                .Where(f => f.Rating == -1)
                                .OrderByDescending(f => f.CreatedAt)
                                .First();
                            return (rep.QuestionText, rep.AnswerText, rep.ReasonText,
                                (IReadOnlyList<string>)(rep.ReasonCategories ?? new List<string>()));
                        })
                        .ToList();

                    feedbackContext = _llm.BuildFeedbackContextPrompt(items);
                    _logger.LogInformation(
                        "[Feedback] {W} warning prompt'a inject edildi (clusters: {C}, candidates: {N})",
                        warnings.Count, clusters.Count, candidates.Count);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Feedback] Personal feedback check atlandı — fail-open");
        }

        if (chunks.Count == 0)
        {
            const string noData = "Sisteme yüklenmiş belgeler arasında bu soruyla ilgili bilgi bulunamadı.";
            yield return new { type = "token", delta = noData };
            var noDataMsg = new ChatMessage
            {
                SessionId = session.Id,
                Role = MessageRole.Assistant,
                Content = noData,
                ResponseToMessageId = userMsg.Id,
            };
            await _uow.Messages.AddAsync(noDataMsg, ct);
            await _uow.SaveChangesAsync(ct);
            yield return new { type = "complete", messageId = noDataMsg.Id, chunks = Array.Empty<object>() };
            yield return new { type = "done" };
            yield break;
        }

        // === 7. LLM streaming ===
        var answerBuilder = new StringBuilder();
        await foreach (var delta in _llm.AskStreamAsync(searchQuestion, chunks, history, feedbackContext, ct))
        {
            answerBuilder.Append(delta);
            yield return new { type = "token", delta };
        }
        var answer = answerBuilder.ToString();

        // === 8. Quality validation + post-process ===
        var quality = await SafeValidateAsync(searchQuestion, chunks, answer, ct);
        var llmRejected = LooksLikeRejection(answer);
        answer = StripNoAnswerMarker(answer);
        answer = StripKaynakReferences(answer);

        string? badge = null;
        if (quality.Score < 0.4)
        {
            _logger.LogWarning("[AnswerQuality][Stream] Kritik düşük skor ({Score:F2})", quality.Score);
            badge = "⚠️ Bu cevap belgelerden derlendi — kritik kararlar için kaynağı doğrulamanızı öneririz.";
        }
        else if (quality.Score < 0.65)
        {
            _logger.LogInformation("[AnswerQuality][Stream] Orta skor ({Score:F2})", quality.Score);
            badge = "ℹ️ Bu cevabın bazı detayları belgelerden tam doğrulanamadı; teyit etmek için kaynaklara göz atabilirsiniz.";
        }

        // === 9. Images + follow-ups (parallel) ===
        List<string> allImagePaths;
        string? imagesJson;
        if (llmRejected)
        {
            allImagePaths = new List<string>();
            imagesJson = null;
        }
        else
        {
            var seenPaths = new HashSet<string>();
            allImagePaths = chunks
                .Where(c => c.ImagePath != null)
                .SelectMany(c =>
                {
                    try
                    {
                        var parsed = JsonSerializer.Deserialize<List<string>>(c.ImagePath!);
                        return parsed ?? new List<string> { c.ImagePath! };
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "[Chat] ImagePath JSON parse hatası — raw path fallback'e düşülüyor");
                        return new List<string> { c.ImagePath! };
                    }
                })
                .Where(p => seenPaths.Add(p))
                .ToList();
            imagesJson = allImagePaths.Count > 0 ? JsonSerializer.Serialize(allImagePaths) : null;
        }

        var followUpTaskFinal = llmRejected
            ? Task.FromResult(new List<string>())
            : SafeFollowUpsAsync(searchQuestion, answer, chunks, ct);

        // === 10. Save assistant message + cache write decision ===
        var assistantMsg = new ChatMessage
        {
            SessionId = session.Id,
            Role = MessageRole.Assistant,
            Content = answer,
            ImagesJson = imagesJson,
            ResponseToMessageId = userMsg.Id,
        };
        await _uow.Messages.AddAsync(assistantMsg, ct);

        var qualityOkForCache = quality.Validated
            && (quality.Score >= 0.65 || (quality.Score >= 0.4 && quality.Issues.Count == 0));
        var willCache = isCacheable && !llmRejected && qualityOkForCache;
        if (willCache)
        {
            try
            {
                await _uow.QuestionCache.UpsertAsync(new QuestionCache
                {
                    QuestionText = searchQuestion,
                    QuestionVector = questionVector,
                    Answer = answer,
                    ImagesJson = imagesJson,
                }, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Cache][Stream] AddAsync hatası — '{Question}'", req.Question);
                willCache = false;
            }
        }

        try
        {
            await _uow.SaveChangesAsync(ct);
            if (willCache)
                _logger.LogInformation("[Cache][Stream] WRITE — '{Question}'", searchQuestion);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Save][Stream] Hata — '{Question}'", req.Question);
        }

        var followUps = await followUpTaskFinal;

        // === 11. Complete + done ===
        yield return new
        {
            type = "complete",
            messageId = assistantMsg.Id,  // ⭐ feedback için gerçek Guid
            chunks = chunks.Adapt<List<AnswerSourceChunkDto>>(),
            images = allImagePaths.Count > 0 ? allImagePaths : null,
            followUps = followUps.Count > 0 ? followUps : null,
            badge,
            quality = quality.Score
        };
        yield return new { type = "done" };
    }

    private const string NoAnswerMarker = "[NO_ANSWER]";

    private static bool LooksLikeRejection(string answer)
    {
        if (string.IsNullOrWhiteSpace(answer)) return true;
        return answer.TrimStart().StartsWith(NoAnswerMarker, StringComparison.Ordinal);
    }

    // LLM bazen prompt'a rağmen "(KAYNAK [N])" referansları sızdırıyor → post-process strip.
    // Hem "(KAYNAK [1])" hem "(KAYNAK 1)" hem "KAYNAK [1]" yakalanır.
    private static readonly System.Text.RegularExpressions.Regex KaynakRefRegex =
        new(@"\s*\(?\s*KAYNAK\s*\[?\s*\d+\s*\]?\s*\)?",
            System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    private static string StripKaynakReferences(string answer) =>
        string.IsNullOrEmpty(answer) ? answer : KaynakRefRegex.Replace(answer, "");

    private static string StripNoAnswerMarker(string answer)
    {
        if (string.IsNullOrWhiteSpace(answer)) return answer;
        var trimmed = answer.TrimStart();
        if (!trimmed.StartsWith(NoAnswerMarker, StringComparison.Ordinal)) return answer;
        return trimmed[NoAnswerMarker.Length..].TrimStart();
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
        var spec = new ChatSessionFilterSpec(
            UserId: _currentUser.UserId,
            Page: page,
            PageSize: pageSize,
            DateFrom: dateFrom,
            DateTo: dateTo,
            SortBy: ChatSessionFilterSpec.ParseSortBy(sortBy),
            Ascending: ascending);

        var paged = await _uow.Sessions.ListAsync(spec, ct);
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
        var cached = await _uow.QuestionCache.GetTopByHitCountAsync(limit, ct);
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

    public async Task<Result<FeedbackResponseDto>> AddFeedbackAsync(
        FeedbackRequestDto request, CancellationToken ct = default)
    {
        // 1. Mesajı çek + yetki kontrolü
        var message = await _uow.Messages.GetByIdAsync(request.MessageId, ct);
        if (message is null)
            return Result<FeedbackResponseDto>.Failure(Error.NotFound("Mesaj bulunamadı."));

        // Sadece asistan mesajlarına feedback verilebilir
        if (message.Role != MessageRole.Assistant)
            return Result<FeedbackResponseDto>.Failure(
                Error.Validation("Sadece asistan cevaplarına geri bildirim verilebilir."));

        var session = await _uow.Sessions.GetByIdAsync(message.SessionId, ct);
        if (session is null || (session.UserId != _currentUser.UserId
                                && !_currentUser.IsInRole(Roles.Admin)))
            return Result<FeedbackResponseDto>.Failure(
                Error.Forbidden("Bu mesaja erişiminiz yok."));

        // 2. UNIQUE check (DB constraint backup, kullanıcı dostu hata)
        var alreadyExists = await _uow.Feedback.ExistsByUserAndMessageAsync(
            _currentUser.UserId, request.MessageId, ct);
        if (alreadyExists)
            return Result<FeedbackResponseDto>.Failure(
                Error.Conflict("Bu mesaja zaten geri bildirim verdiniz."));

        // 3. Soru metnini çek — ResponseToMessageId FK ile DİREKT bağlantı.
        //    Migration öncesi eski mesajlarda FK null → feedback verilemez (kullanıcı sohbeti siler).
        if (!message.ResponseToMessageId.HasValue)
            return Result<FeedbackResponseDto>.Failure(
                Error.Validation("Bu mesaja geri bildirim verilemez (eski mesaj). Sohbeti silip yeniden sorabilirsiniz."));

        var userMsg = await _uow.Messages.GetByIdAsync(message.ResponseToMessageId.Value, ct);
        if (userMsg is null)
            return Result<FeedbackResponseDto>.Failure(
                Error.NotFound("Sorulan soru bulunamadı."));

        var questionText = userMsg.Content;

        // 4. Soru metnini embed et — gelecek sorgularda similarity matching için
        var questionVector = await _embeddingService.GetEmbeddingAsync(questionText, ct);

        // 5. Feedback kaydet (chunk match yok, sadece question similarity ile çalışır)
        var feedback = new ChatMessageFeedback
        {
            UserId = _currentUser.UserId,
            MessageId = request.MessageId,
            SessionId = session.Id,
            QuestionText = questionText,
            QuestionVector = questionVector,
            AnswerText = message.Content,
            Rating = request.Rating,
            ReasonCategories = request.Categories?.ToList() ?? new List<string>(),
            ReasonText = string.IsNullOrWhiteSpace(request.ReasonText) ? null : request.ReasonText,
        };
        await _uow.Feedback.AddAsync(feedback, ct);

        try
        {
            await _uow.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (IsUniqueConstraintViolation(ex))
        {
            // Race condition — başka request aynı anda yazdı
            return Result<FeedbackResponseDto>.Failure(
                Error.Conflict("Bu mesaja zaten geri bildirim verdiniz."));
        }

        _logger.LogInformation(
            "[Feedback] User={U} Message={M} Rating={R}",
            _currentUser.UserId, request.MessageId, request.Rating);

        return Result<FeedbackResponseDto>.Success(
            new FeedbackResponseDto(feedback.Id, feedback.CreatedAt));
    }

    private static bool IsUniqueConstraintViolation(Exception ex)
    {
        var inner = ex.InnerException ?? ex;
        return inner.GetType().FullName == "Npgsql.PostgresException"
            && inner.GetType().GetProperty("SqlState")?.GetValue(inner) as string == "23505";
    }

    // Feedback clustering için BGE-M3 vector benzerliği (1024-dim).
    // İki feedback'in QuestionVector'ünden cosine similarity.
    private static double CosineSimilarity(float[] a, float[] b)
    {
        if (a is null || b is null || a.Length != b.Length || a.Length == 0) return 0.0;
        double dot = 0, na = 0, nb = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            na += a[i] * a[i];
            nb += b[i] * b[i];
        }
        var denom = Math.Sqrt(na) * Math.Sqrt(nb);
        return denom == 0 ? 0.0 : dot / denom;
    }
}

