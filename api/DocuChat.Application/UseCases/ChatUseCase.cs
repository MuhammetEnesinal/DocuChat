using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Mapster;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using DocuChat.Application.Interfaces.UseCases;
using DocuChat.Application.Interfaces.Services;
using DocuChat.Application.Interfaces.Repositories;
using DocuChat.Application.Common.Results;
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
    private readonly IDbExceptionInspector _dbExceptionInspector;
    private readonly ILogger<ChatUseCase> _logger;
    private readonly double _cacheSimilarityThreshold;
    private readonly double _cacheHighConfidenceThreshold;
    private readonly double _feedbackDislikeBypassThreshold;
    private readonly int _historyTokenBudget;
    private readonly int _historyMaxMessages;
    private readonly int _historyKeepRawCount;
    private readonly bool _followUpsEnabled;

    public ChatUseCase(
        IUnitOfWork uow,
        IRetrievalPipeline retrieval,
        ILlmService llm,
        ICurrentUser currentUser,
        IEmbeddingService embeddingService,
        ITokenCounter tokenCounter,
        IDbExceptionInspector dbExceptionInspector,
        ILogger<ChatUseCase> logger,
        IConfiguration configuration)
    {
        _uow = uow;
        _retrieval = retrieval;
        _llm = llm;
        _currentUser = currentUser;
        _embeddingService = embeddingService;
        _tokenCounter = tokenCounter;
        _dbExceptionInspector = dbExceptionInspector;
        _logger = logger;
        _cacheSimilarityThreshold = configuration.GetValue("Cache:SimilarityThreshold", 0.87);
        _cacheHighConfidenceThreshold = configuration.GetValue("Cache:HighConfidenceThreshold", 0.95);
        // Dislike-based cache bypass: kullanıcı 0.85'ten yüksek benzerlikte bir cevaba dislike
        // verdiyse cache atlanır. Sıkı eşik → false positive minimal (benzer ama farklı sorularda
        // bypass'a takılma).
        _feedbackDislikeBypassThreshold = configuration.GetValue("Cache:FeedbackDislikeBypassThreshold", 0.85);
        _historyTokenBudget = configuration.GetValue("Chat:HistoryTokenBudget", 3000);
        _historyMaxMessages = configuration.GetValue("Chat:HistoryMaxMessages", 20);
        // Son N mesaj HAM gönderilir, öncesi LLM ile özetlenir. 6 = 3 turn (3 user + 3 assistant)
        _historyKeepRawCount = Math.Max(2, configuration.GetValue("Chat:KeepRawMessages", 6));
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

    // LLM cevabını token token yield eden chat akışı. Kalite skoru düşükse cache yazılmaz,
    // kullanıcıya uyarı badge'i gösterilir.
    public async IAsyncEnumerable<object> AskStreamAsync(
        AskRequestDto req,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        // Oturumu bul veya yeni oluştur. sessionCreatedThisRequest: bu istekte YENİ session açıldıysa
        // (ilk soru). İlk soru iptal edilirse boş session geride kalmasın diye silmek için işaretlenir.
        var sessionCreatedThisRequest = !req.SessionId.HasValue;
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

        // Geçmiş yükleme: son N mesaj ham tutulur, daha eskiler LLM ile özetlenip system
        // rolünde eklenir. Böylece anahtar bağlam (kullanıcı rolü, konu, terimler) bütçe
        // dolduğunda da korunur. N = Chat:KeepRawMessages config'i.
        var history = new List<(string Role, string Content)>();
        var sessionWithMessages = await _uow.Sessions.GetWithMessagesAsync(session.Id, ct);
        if (sessionWithMessages?.Messages?.Any() == true)
        {
            var allMessages = sessionWithMessages.Messages
                .OrderBy(m => m.CreatedAt)
                .ToList();

            // Son N mesaj → ham
            var rawCount = Math.Min(_historyKeepRawCount, allMessages.Count);
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

        // Soru embedding'i
        var questionVector = await _embeddingService.GetEmbeddingAsync(searchQuestion, ct);

        // Cache araması
        var cacheMatch = await _uow.QuestionCache.FindSimilarAsync(questionVector, _cacheSimilarityThreshold, ct);
        if (cacheMatch is not null)
        {
            // Kullanıcıya özel feedback net skoru (global cache'i değiştirmez):
            //   net > 0 → dislike baskın → cache atlanır, taze cevap üretilir
            //   net < 0 → like baskın    → doğrulama atlanır, hızlı cevap döner
            //   net = 0 → nötr           → benzerliğe göre hızlı veya doğrulamalı
            var feedbackNet = await _uow.Feedback.GetSimilarFeedbackNetAsync(
                _currentUser.UserId, questionVector, _feedbackDislikeBypassThreshold, ct);
            if (feedbackNet > 0)
            {
                _logger.LogInformation(
                    "[Cache][Stream] BYPASS — User {U} net dislike={Net}, cache atlanıyor",
                    _currentUser.UserId, feedbackNet);
            }
            else
            {
            var hit = cacheMatch.Cache;
            string? validated;
            if (cacheMatch.Similarity >= _cacheHighConfidenceThreshold || feedbackNet < 0)
            {
                if (feedbackNet < 0)
                    _logger.LogInformation(
                        "[Cache][Stream] HIT FAST (like override) sim={Sim:F3} net={Net}",
                        cacheMatch.Similarity, feedbackNet);
                else
                    _logger.LogInformation("[Cache][Stream] HIT FAST sim={Sim:F3}", cacheMatch.Similarity);
                validated = hit.Answer;
            }
            else
            {
                // Kullanıcının önceki dislike feedback'leri doğrulama LLM'ine verilir; karar
                // bu şikayetleri dikkate alarak alınır.
                var validateFeedbackCtx = await BuildUserFeedbackContextAsync(questionVector, ct);
                validated = await _llm.ValidateCachedAnswerAsync(
                    searchQuestion, hit.QuestionText, hit.Answer, history, validateFeedbackCtx, ct);
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
                session.UpdatedAt = DateTime.UtcNow;  // son aktivite → sidebar'da (sabitlerin altında) en üste taşır
                await _uow.SaveChangesAsync(ct);

                var hitFollowUps = await followUpTask;
                yield return new
                {
                    type = "cache_hit",
                    messageId = assistantMsgCache.Id,  // feedback için gerçek mesaj Guid'i
                    answer = hit.Answer,
                    images = hitImgs.Count > 0 ? hitImgs : null,
                    followUps = hitFollowUps.Count > 0 ? hitFollowUps : null
                };
                yield return new { type = "done" };
                yield break;
            }
            }
        }

        // Cache yok: önbelleğe alınabilirlik kontrolü + gerekirse netleştirme sorusu
        var docNamesWithSummary = await _uow.Documents.GetDocumentNamesAndSummariesAsync(ct);
        var docNameStrings = docNamesWithSummary.Select(d => d.FileName).ToList();

        // IsCacheable LLM helper'ına bağımlı. Helper çökerse chat'in tamamı durmasın diye
        // fail-open: geçmiş yoksa cacheable=true (ilk soru), varsa cacheable=false
        // (güvenli taraf — cache yazımı atlanır ama cevap yine üretilir).
        bool isCacheable;
        if (history.Count == 0)
        {
            isCacheable = true;
        }
        else
        {
            try { isCacheable = await _llm.IsCacheableAsync(req.Question, history, ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[Cache][Stream] IsCacheable LLM fail — fail-open: cacheable=false");
                isCacheable = false;
            }
        }
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

        // Netleştirme yok: kullanıcı mesajını kaydet. Asistan mesajı sonra
        // ResponseToMessageId = userMsg.Id ile buna bağlanır.
        var userMsg = new ChatMessage
        {
            SessionId = session.Id,
            Role = MessageRole.User,
            Content = req.Question
        };
        await _uow.Messages.AddAsync(userMsg, ct);
        await _uow.SaveChangesAsync(ct);

        // Bu noktadan sonra iptal/kopma olursa (controller iterator'ı DISPOSE eder ya da arama/
        // streaming OCE fırlatır) asistan cevabı kaydedilmemiş demektir. Güvenilir temizlik için
        // try/finally: finally normal bitişte, dispose'da ve exception'da çalışır. Tam alışveriş
        // kaydedilmediyse (exchangeSaved=false) VE iptal edildiyse yetim user mesajı + (bu istekte
        // açılan) boş session silinir → DB'de/sidebar'da mesajsız ölü kayıt kalmaz. (Düz cancel-check'e
        // güvenilmez: iptalde iterator bir yield'de asılı kalıp dispose edilir, oraya hiç gelinmez.)
        var exchangeSaved = false;
        try
        {

        // Arama göstergesi: arama+rerank ilk token'a kadar birkaç saniye sürer; kullanıcı
        // boş imleç yerine "Belgeler aranıyor" görür. İlk token gelince frontend temizler.
        yield return new { type = "searching" };

        _logger.LogInformation("[Cache][Stream] IsCacheable kararı → {Result}", isCacheable);
        // questionVector = embed(searchQuestion) yukarıda cache için hesaplandı. Standalone
        // soruda retrieval de ham soruyu embed edeceğinden aynı vektörü geçirip 2. çağrıyı önleriz
        // (takip sorusunda boost metni farklı olduğu için VectorSearch yine de yeniden embed eder).
        var chunks = (await _retrieval.SearchAsync(
            searchQuestion, history,
            isStandalone: isCacheable,
            precomputedQueryVector: questionVector,
            ct: ct)).ToList();

        // Kullanıcının soru-benzerliğine göre geçmiş feedback bağlamı (cache hit doğrulama
        // yolu da aynı yardımcıyı kullanır).
        var feedbackContext = await BuildUserFeedbackContextAsync(questionVector, ct);

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
            exchangeSaved = true;  // tam alışveriş (soru+cevap) kaydedildi → finally temizlemez
            yield return new { type = "complete", messageId = noDataMsg.Id };
            yield return new { type = "done" };
            yield break;
        }

        // Üretim göstergesi: arama bitti, LLM yazmaya başlıyor. İlk token gelince temizlenir.
        yield return new { type = "generating" };

        // Context'i stream'den ÖNCE kur: görsel işaretleri ([IMG:N] → global [[IMG-K]]) deterministik
        // haritaya bağlanır. Harita cevap sonrası [[IMG-K]] → gerçek görsel render için kullanılır.
        var answerContext = _llm.BuildAnswerContext(chunks);

        // "Cevap yok" marker'ı ([NO_ANSWER]) cevabın EN BAŞINDA gelir. Ham haliyle akarsa kullanıcı
        // marker'ı görür (titreme) ve complete kaçarsa kalıcı olur. Bu yüzden ilk ~32 karakteri
        // tamponla, marker'ı temizleyip öyle yay; gerisi düz akış. (32 > marker + olası sarma.)
        var answerBuilder = new StringBuilder();
        var prefixEmitted = false;
        const int markerGate = 32;
        await foreach (var delta in _llm.StreamAnswerAsync(answerContext, searchQuestion, history, feedbackContext, ct))
        {
            answerBuilder.Append(delta);
            if (!prefixEmitted)
            {
                if (answerBuilder.Length < markerGate) continue;  // yeterli birikene kadar tut
                prefixEmitted = true;
                var cleanedPrefix = StripNoAnswerMarker(answerBuilder.ToString());
                if (cleanedPrefix.Length > 0)
                    yield return new { type = "token", delta = cleanedPrefix };
            }
            else
            {
                yield return new { type = "token", delta };
            }
        }
        // Stream markerGate'ten kısa bittiyse (çok kısa cevap) biriken tamponu temizleyip yay.
        if (!prefixEmitted && answerBuilder.Length > 0)
        {
            var cleanedPrefix = StripNoAnswerMarker(answerBuilder.ToString());
            if (cleanedPrefix.Length > 0)
                yield return new { type = "token", delta = cleanedPrefix };
        }

        // Streaming döngüsü ct iptalinde OCE fırlatmadan bittiyse burada yakalanır: yarım cevabı
        // işleme/kaydetme; erken çık. Temizlik finally'de yapılır (yield break finally'yi çalıştırır).
        if (ct.IsCancellationRequested)
            yield break;
        var answer = answerBuilder.ToString();

        // Önce post-process, sonra kalite doğrulama: doğrulama kullanıcıya gösterilen temiz
        // cevabı değerlendirir, böylece skor ile içerik tutarlı olur.
        var llmRejected = LooksLikeRejection(answer);
        answer = StripNoAnswerMarker(answer);
        answer = StripKaynakReferences(answer);

        // Görselleri KOD yerleştirir: LLM'in koruduğu [[IMG-K]] işaretleri → gerçek görsel markdown'ı.
        // LLM görseli SEÇMEZ; sadece içerikteki işareti korur, kod gerçek görsele çevirir (deterministik).
        List<string> allImagePaths;
        if (llmRejected)
        {
            answer = StripImageMarkers(answer);
            allImagePaths = new List<string>();
        }
        else
        {
            answer = ResolveImageMarkers(answer, answerContext.ImageMap, out allImagePaths);

            // Güvenlik ağı: LLM tüm işaretleri atladıysa (cevapta hiç görsel çözülmediyse) ama
            // context'te görsel vardı → kullanıcı görseli kaybetmesin, galeri panelinde göster.
            if (allImagePaths.Count == 0 && answerContext.ImageMap.Count > 0)
            {
                allImagePaths = answerContext.ImageMap.Values
                    .Select(v => v.Path).Distinct(StringComparer.Ordinal).ToList();
                _logger.LogWarning(
                    "[Görsel] LLM hiç [[IMG-N]] işareti korumadı — {Count} görsel galeri olarak eklendi (güvenlik ağı)",
                    allImagePaths.Count);
            }
        }

        answer = NormalizeImageMarkdown(answer);
        var quality = await SafeValidateAsync(searchQuestion, chunks, answer, ct);

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

        // Galeri: cevapta KOD'un yerleştirdiği görseller (+ güvenlik ağı). allImagePaths yukarıda
        // [[IMG-K]] çözümünden hesaplandı; burada yalnızca persist için JSON'a serialize edilir.
        var imagesJson = allImagePaths.Count > 0 ? JsonSerializer.Serialize(allImagePaths) : null;

        var followUpTaskFinal = llmRejected
            ? Task.FromResult(new List<string>())
            : SafeFollowUpsAsync(searchQuestion, answer, chunks, ct);

        // Asistan mesajını kaydet + cache yazma kararı
        var assistantMsg = new ChatMessage
        {
            SessionId = session.Id,
            Role = MessageRole.Assistant,
            Content = answer,
            ImagesJson = imagesJson,
            ResponseToMessageId = userMsg.Id,
        };
        await _uow.Messages.AddAsync(assistantMsg, ct);
        session.UpdatedAt = DateTime.UtcNow;  // son aktivite → sidebar'da (sabitlerin altında) en üste taşır

        var qualityOkForCache = quality.Validated
            && (quality.Score >= 0.65 || (quality.Score >= 0.4 && quality.Issues.Count == 0));
        var willCache = isCacheable && !llmRejected && qualityOkForCache;
        if (willCache)
        {
            try
            {
                // Cevabın hangi belgelerden üretildiği kaydedilir; belge silindiğinde sadece
                // ilgili cache entry'leri temizlenir, tüm cache uçmaz.
                var sourceDocIds = chunks
                    .Where(c => c.DocumentId.HasValue)
                    .Select(c => c.DocumentId!.Value.ToString())
                    .Distinct()
                    .ToList();
                var sourceDocCsv = sourceDocIds.Count > 0 ? string.Join(",", sourceDocIds) : null;

                await _uow.QuestionCache.UpsertAsync(new QuestionCache
                {
                    QuestionText = searchQuestion,
                    QuestionVector = questionVector,
                    Answer = answer,
                    ImagesJson = imagesJson,
                    SourceDocumentIds = sourceDocCsv,
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
            exchangeSaved = true;  // asistan cevabı DB'ye yazıldı → finally temizlemez
            if (willCache)
                _logger.LogInformation("[Cache][Stream] WRITE — '{Question}'", searchQuestion);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Save][Stream] Hata — '{Question}'", req.Question);
        }

        var followUps = await followUpTaskFinal;

        yield return new
        {
            type = "complete",
            messageId = assistantMsg.Id,  // feedback için gerçek mesaj Guid'i
            // Final içerik: stream sırasında ham [[IMG-N]] işaretleri aktı; burada KOD'un görsel
            // markdown'ına çevirdiği + temizlediği nihai metni gönderip frontend'in içeriği
            // değiştirmesini sağlıyoruz (ham işaret kullanıcıda kalmasın).
            content = answer,
            images = allImagePaths.Count > 0 ? allImagePaths : null,
            followUps = followUps.Count > 0 ? followUps : null,
            badge,
            quality = quality.Score
        };
        yield return new { type = "done" };

        }
        finally
        {
            // İptal/kopma olduysa VE tam alışveriş kaydedilmediyse: yetim user mesajı + (bu istekte
            // açılan) boş session silinir. finally normal bitişte, dispose'da ve exception'da çalışır →
            // iptal hangi anda olursa olsun (arama, streaming) DB'de mesajsız session/yetim mesaj kalmaz.
            // Mevcut session'a yazılıp iptal edilirse session KORUNUR (eski mesajları var), sadece bu
            // yetim user mesajı silinir. Best-effort: iptal edilmiş ct ile çalışmaz → None ile.
            if (!exchangeSaved && ct.IsCancellationRequested)
            {
                try
                {
                    _uow.Messages.Delete(userMsg);
                    if (sessionCreatedThisRequest) _uow.Sessions.Delete(session);
                    await _uow.SaveChangesAsync(CancellationToken.None);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[Stream] İptal temizliği başarısız — yetim kayıt kalmış olabilir");
                }
            }
        }
    }

    // "Cevap yok" marker'ı. Prompt'ta `[NO_ANSWER]` istenir ama LLM pratikte varyant üretebiliyor:
    // **[NO_ANSWER]**, `[NO_ANSWER]`, [no_answer], [NO ANSWER], [NO-ANSWER], sonuna : / . vb.
    // Bu yüzden tespit TOLERANSLI ve anchored (^): baştaki opsiyonel markdown/backtick + boşluk/
    // tire varyantları, büyük/küçük harf duyarsız. Yalnızca cevabın EN BAŞINDAKİ marker yakalanır.
    private static readonly System.Text.RegularExpressions.Regex NoAnswerMarkerRegex =
        new(@"^\s*[`*_~]*\[\s*NO[\s_\-]?ANSWER\s*\][`*_~:.\-–—]*\s*",
            System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    private static bool LooksLikeRejection(string answer)
    {
        if (string.IsNullOrWhiteSpace(answer)) return true;
        return NoAnswerMarkerRegex.IsMatch(answer);
    }

    // LLM bazen prompt'a rağmen "(KAYNAK [N])" referansları sızdırıyor → post-process strip.
    // Hem "(KAYNAK [1])" hem "(KAYNAK 1)" hem "KAYNAK [1]" yakalanır.
    private static readonly System.Text.RegularExpressions.Regex KaynakRefRegex =
        new(@"\s*\(?\s*KAYNAK\s*\[?\s*\d+\s*\]?\s*\)?",
            System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    private static string StripKaynakReferences(string answer) =>
        string.IsNullOrEmpty(answer) ? answer : KaynakRefRegex.Replace(answer, "");

    // LLM bazen image markdown'ı bozuk üretir (nested, multi-line, URL'de boşluk).
    // Frontend de aynısını yapıyor ama DB'ye temiz versiyon yazılsın → mesaj history reload'da düzgün.
    private static readonly System.Text.RegularExpressions.Regex NestedImageMdRegex =
        new(@"!\[[^\]]*\]\(\s*(!\[[^\]]*\]\([^)]+\))\s*\)",
            System.Text.RegularExpressions.RegexOptions.Compiled);
    private static readonly System.Text.RegularExpressions.Regex ImageMdWhitespaceRegex =
        new(@"!\[([^\]]*)\]\(\s*([^)]+?)\s*\)",
            System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.Singleline);
    private static readonly System.Text.RegularExpressions.Regex UrlInternalWsRegex =
        new(@"\s+", System.Text.RegularExpressions.RegexOptions.Compiled);

    // LLM cevabındaki görsel işareti [[IMG-K]] veya [[IMG-K: açıklama]]. Esnek: LLM işareti hafif
    // bozsa da ([IMG-3], [[IMG-3: ...], [[IMG 3]]) yakalanır — "güvenlik ağı" niteliğinde tolerans.
    // (\d+) sonrası [^\]]* açıklama kısmını (varsa) yutar.
    // Baştaki/sondaki opsiyonel backtick de yutulur: LLM işareti bazen `[[IMG-3]]` gibi kod
    // bloğuna sokuyor → çevrim sonrası `![](url)` inline-code olur, resim render edilmez. Yut.
    private static readonly System.Text.RegularExpressions.Regex ImgRefMarkerRegex =
        new(@"`?\[\[?\s*IMG[-:\s]?\s*(\d+)[^\]]*\]\]?`?",
            System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    // LLM'in koruduğu [[IMG-K]] işaretlerini gerçek görsel markdown'ına ( ![caption](/uploads/path) )
    // çevirir — görseli KOD yerleştirir, LLM değil (deterministik). Bilinmeyen numara → işaret silinir
    // (kullanıcı ham marker görmesin). out usedPaths: cevapta görünen görseller (sıra korunur, dedup).
    private static string ResolveImageMarkers(
        string answer, IReadOnlyDictionary<int, AnswerImageRef> imageMap, out List<string> usedPaths)
    {
        var used = new List<string>();
        if (string.IsNullOrEmpty(answer)) { usedPaths = used; return answer; }
        if (imageMap.Count == 0) { usedPaths = used; return StripImageMarkers(answer); }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = ImgRefMarkerRegex.Replace(answer, m =>
        {
            if (!int.TryParse(m.Groups[1].Value, out var k) || !imageMap.TryGetValue(k, out var img))
                return string.Empty;  // bilinmeyen/uydurma numara → işareti kaldır

            var alt = string.IsNullOrWhiteSpace(img.Caption)
                ? "gorsel"
                : img.Caption!.Replace("[", "(").Replace("]", ")").Replace("\n", " ").Trim();
            if (alt.Length > 200) alt = alt[..200].TrimEnd() + "...";

            if (seen.Add(img.Path)) used.Add(img.Path);
            return $"![{alt}](/uploads/{img.Path})";
        });
        usedPaths = used;
        return result;
    }

    // Cevapta kalan/çözülmeyen [[IMG-K]] artıklarını temizler (reddedilen cevap veya görsel yokken).
    private static string StripImageMarkers(string answer) =>
        string.IsNullOrEmpty(answer) ? answer : ImgRefMarkerRegex.Replace(answer, string.Empty);

    private static string NormalizeImageMarkdown(string answer)
    {
        if (string.IsNullOrEmpty(answer)) return answer;
        var out_ = answer;
        // Nested wrap'i temizle (birden fazla katman olabilir → bounded loop)
        for (var i = 0; i < 5 && NestedImageMdRegex.IsMatch(out_); i++)
            out_ = NestedImageMdRegex.Replace(out_, "$1");
        // URL içindeki whitespace/newline'ı temizle
        out_ = ImageMdWhitespaceRegex.Replace(out_, m =>
            $"![{m.Groups[1].Value}]({UrlInternalWsRegex.Replace(m.Groups[2].Value, "")})");
        return out_;
    }

    private static string StripNoAnswerMarker(string answer)
    {
        if (string.IsNullOrWhiteSpace(answer)) return answer;
        return NoAnswerMarkerRegex.Replace(answer, string.Empty, 1).TrimStart();
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
        bool? archived = false,
        CancellationToken ct = default)
    {
        var spec = new ChatSessionFilterSpec(
            UserId: _currentUser.UserId,
            Page: page,
            PageSize: pageSize,
            DateFrom: dateFrom,
            DateTo: dateTo,
            SortBy: ChatSessionFilterSpec.ParseSortBy(sortBy),
            Ascending: ascending,
            Archived: archived);

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
        session.UpdatedAt = DateTime.UtcNow;
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

    // ── Archive / Pin ── tüm yetki kontrolü + status toggle aynı pattern, helper ile DRY.
    public Task<Result<bool>> ArchiveSessionAsync(Guid sessionId, CancellationToken ct = default) =>
        UpdateSessionStateAsync(sessionId, s =>
        {
            s.IsArchived = true;
            s.ArchivedAt = DateTime.UtcNow;
            // Arşive giderken pin'i kaldır — arşivde sabitlemenin anlamı yok
            s.IsPinned = false;
            s.PinnedAt = null;
        }, "Archive", ct);

    public Task<Result<bool>> UnarchiveSessionAsync(Guid sessionId, CancellationToken ct = default) =>
        UpdateSessionStateAsync(sessionId, s =>
        {
            s.IsArchived = false;
            s.ArchivedAt = null;
        }, "Unarchive", ct);

    public Task<Result<bool>> PinSessionAsync(Guid sessionId, CancellationToken ct = default) =>
        UpdateSessionStateAsync(sessionId, s =>
        {
            s.IsPinned = true;
            s.PinnedAt = DateTime.UtcNow;
            // Pin'lerken arşivden çıkar — sabit görünür olmalı
            if (s.IsArchived) { s.IsArchived = false; s.ArchivedAt = null; }
        }, "Pin", ct);

    public Task<Result<bool>> UnpinSessionAsync(Guid sessionId, CancellationToken ct = default) =>
        UpdateSessionStateAsync(sessionId, s =>
        {
            s.IsPinned = false;
            s.PinnedAt = null;
        }, "Unpin", ct);

    private async Task<Result<bool>> UpdateSessionStateAsync(
        Guid sessionId, Action<ChatSession> mutate, string opName, CancellationToken ct)
    {
        var session = await _uow.Sessions.GetByIdAsync(sessionId, ct);
        if (session is null)
            return Result<bool>.Failure(Error.NotFound("Oturum bulunamadı."));
        if (session.UserId != _currentUser.UserId && !_currentUser.IsInRole(Roles.Admin))
            return Result<bool>.Failure(Error.Forbidden("Bu oturuma erişiminiz yok."));

        mutate(session);
        session.UpdatedAt = DateTime.UtcNow;
        await _uow.SaveChangesAsync(ct);
        _logger.LogInformation("[{Op}] Session {SessionId} (User: {UserId})", opName, sessionId, _currentUser.UserId);
        return Result<bool>.Success(true);
    }

    public async Task<Result<int>> GetArchivedCountAsync(CancellationToken ct = default)
    {
        var count = await _uow.Sessions.GetArchivedCountAsync(_currentUser.UserId, ct);
        return Result<int>.Success(count);
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

        // 3. Soru metnini ResponseToMessageId FK üzerinden çek. FK boşsa (çok eski mesaj)
        //    feedback verilemez.
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

        // 5. Feedback kaydet (soru benzerliği ile eşleşir, chunk eşleşmesi tutulmaz)
        var feedback = new ChatMessageFeedback
        {
            UserId = _currentUser.UserId,
            MessageId = request.MessageId,
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
        catch (Exception ex) when (_dbExceptionInspector.IsUniqueConstraintViolation(ex))
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

    // Kullanıcının son 6 aydaki dislike feedback'lerini soru-benzerliğine göre kümeler ve en
    // çok şikayet edilen kümeleri LLM system prompt bölümü olarak hazırlar. Benzerlik için
    // QuestionVector'ler üzerinden cosine similarity kullanılır.
    private async Task<string?> BuildUserFeedbackContextAsync(float[] questionVector, CancellationToken ct)
    {
        const double SimilarityThreshold = 0.75;
        const double ClusterThreshold = 0.85;
        const int MaxCandidates = 30;
        const int MaxWarnings = 10;

        try
        {
            var candidates = await _uow.Feedback.GetSimilarFeedbacksAsync(
                _currentUser.UserId, questionVector, SimilarityThreshold,
                maxAgeMonths: 6, MaxCandidates, ct);
            if (candidates.Count == 0) return null;

            // C# tarafında clustering — birbirine çok benzer feedback'leri grupla
            var clusters = new List<List<ChatMessageFeedback>>();
            foreach (var fb in candidates)
            {
                var matched = clusters.FirstOrDefault(cl =>
                    CosineSimilarity(cl[0].QuestionVector, fb.QuestionVector) > ClusterThreshold);
                if (matched != null) matched.Add(fb);
                else clusters.Add(new List<ChatMessageFeedback> { fb });
            }

            // Net dislike skoru — like'lar dislike'ları iptal eder
            var warnings = clusters
                .Select(cl => new
                {
                    Cluster = cl,
                    Net = cl.Count(f => f.Rating == -1) - cl.Count(f => f.Rating == 1),
                })
                .Where(x => x.Net > 0)
                .OrderByDescending(x => x.Net)
                .Take(MaxWarnings)
                .ToList();
            if (warnings.Count == 0) return null;

            var items = warnings.Select(w =>
            {
                var rep = w.Cluster
                    .Where(f => f.Rating == -1)
                    .OrderByDescending(f => f.CreatedAt)
                    .First();
                return (rep.QuestionText, rep.AnswerText, rep.ReasonText,
                    (IReadOnlyList<string>)(rep.ReasonCategories ?? new List<string>()));
            }).ToList();

            _logger.LogInformation(
                "[Feedback] {W} warning hazırlandı (clusters: {C}, candidates: {N})",
                warnings.Count, clusters.Count, candidates.Count);
            return _llm.BuildFeedbackContextPrompt(items);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Feedback] Personal feedback check atlandı — fail-open");
            return null;
        }
    }

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

