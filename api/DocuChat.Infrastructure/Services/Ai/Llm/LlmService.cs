using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using DocuChat.Application.Common.Imaging;
using DocuChat.Application.Interfaces.Services;
using DocuChat.Application.ServiceContracts;
using DocuChat.Infrastructure.Services.Ai.Llm.Helpers;
using DocuChat.Infrastructure.Services.Ai.Llm.Http;
using DocuChat.Infrastructure.Services.Ai.Llm.Prompts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace DocuChat.Infrastructure.Services.Ai.Llm;

// LLM orchestration — prompt seçimi + payload kurulumu + yanıt parse.
// HTTP/retry: MistralChatClient. Prompt metinleri: LlmPrompts. Görsel: ImageResizer.
public class LlmService : ILlmService
{
    private readonly MistralChatClient _client;
    private readonly IConfiguration _cfg;
    private readonly ITokenCounter _tokenCounter;
    private readonly ILogger<LlmService> _logger;

    // LLM'e gönderilecek context için chunk seçim tavanları (sabit adet yerine token bütçesi).
    private readonly int _llmContextTokenBudget;
    private readonly int _llmMaxChunks;

    public LlmService(HttpClient http, IHttpClientFactory httpFactory, IConfiguration cfg, ITokenCounter tokenCounter, ILogger<LlmService> logger)
    {
        _cfg = cfg;
        _tokenCounter = tokenCounter;
        _logger = logger;
        _client = new MistralChatClient(http, httpFactory, cfg, logger);
        // Context bütçesi: dinamik chunk seçimi tavanları (appsettings VectorSearch bölümü).
        _llmContextTokenBudget = cfg.GetValue<int>("VectorSearch:LlmContextTokenBudget", 12000);
        _llmMaxChunks = Math.Max(1, cfg.GetValue<int>("VectorSearch:LlmMaxChunks", 16));
    }

    // Streaming variant — token delta'larını üretir. OpenAI-compat dışındaki provider'lar için
    // (Anthropic/Gemini) tam cevap tek delta olarak döner (fallback). Ollama'nın kendi streaming
    // formatı OpenAI'dan farklı olduğu için onu da non-streaming'e düşürdük.
    public async IAsyncEnumerable<string> AskStreamAsync(
        string question,
        IEnumerable<ChunkResult> contextChunks,
        IEnumerable<(string Role, string Content)>? history = null,
        string? feedbackContext = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        // Token bütçeli dinamik chunk seçimi: chunk'lar sırayla eklenir, toplam token bütçesi
        // veya adet tavanı aşılana kadar devam edilir. İlk chunk her zaman alınır (tek büyük
        // chunk olsa bile boş cevaba düşmemek için). Böylece küçük chunk'larda daha çok kaynak
        // sığar, büyük chunk'larda context taşması önlenir.
        var candidateChunks = contextChunks
            .Where(c => !string.IsNullOrWhiteSpace(c.Content) && c.Content.Trim().Length > 20);

        var chunkList = new List<ChunkResult>();
        var usedTokens = 0;
        foreach (var c in candidateChunks)
        {
            var chunkTokens = _tokenCounter.Count(c.Content);
            if (chunkList.Count > 0 &&
                (usedTokens + chunkTokens > _llmContextTokenBudget || chunkList.Count >= _llmMaxChunks))
                break;
            chunkList.Add(c);
            usedTokens += chunkTokens;
        }

        if (chunkList.Count == 0)
        {
            yield return "Sisteme yuklenmis belgeler arasinda bu soruyla ilgili bilgi bulunamadi.";
            yield break;
        }

        _logger.LogInformation(
            "[LLM] Context seçimi: {Count} chunk, ~{Tokens} token (bütçe {Budget}, tavan {Max})",
            chunkList.Count, usedTokens, _llmContextTokenBudget, _llmMaxChunks);

        var (context, imageUrls) = BuildContextAndImages(chunkList);
        var historyMessages = TrimHistory(history);
        var userMessage = LlmPrompts.Answer.User(context, question);

        // System prompt'a kullanıcının eski feedback'leri (varsa) inject edilir.
        // Mevcut prompt + ek section — pattern her provider için aynı.
        var systemPrompt = string.IsNullOrWhiteSpace(feedbackContext)
            ? LlmPrompts.Answer.System
            : LlmPrompts.Answer.System + "\n\n" + feedbackContext;

        // Sadece OpenAI-compat provider'da gerçek streaming yap; diğerlerinde non-streaming sync call
        if (_client.MainProvider is "Anthropic" or "Gemini" or "Ollama")
        {
            var full = _client.MainProvider switch
            {
                "Anthropic" => await _client.CallAnthropicAsync(systemPrompt, userMessage, historyMessages, ct),
                "Gemini"    => await _client.CallGeminiAsync(systemPrompt, userMessage, historyMessages, ct),
                _           => await _client.CallOllamaAsync(systemPrompt, userMessage, ct)
            };
            yield return full;
            yield break;
        }

        await foreach (var delta in _client.CallOpenAiWithVisionStreamingAsync(
            systemPrompt, userMessage, imageUrls, historyMessages, ct))
        {
            yield return delta;
        }
    }

    // Chunk içeriklerini KAYNAK bloklarına çevirir + LLM'e standart MARKDOWN IMAGE syntax verir.
    // [IMG:N — caption] (chunk-local) → ![caption](/uploads/path) (markdown).
    // LLM eğitim verisinde milyonlarca markdown image örneği gördüğü için DOĞAL davranır;
    // frontend ReactMarkdown standart <img> render eder, URL kullanıcıya görünmez.
    private static readonly System.Text.RegularExpressions.Regex ImgMarkerRegex =
        new(@"\[IMG:(\d+)([^\]]*)\]", System.Text.RegularExpressions.RegexOptions.Compiled);

    private (string Context, List<string> ImageUrls) BuildContextAndImages(IReadOnlyList<ChunkResult> chunkList)
    {
        var chunkImageLists = chunkList.Select(c =>
        {
            if (string.IsNullOrWhiteSpace(c.ImagePath)) return new List<string>();
            try { return JsonSerializer.Deserialize<List<string>>(c.ImagePath) ?? new(); }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[LLM] ImagePath JSON parse hatası — değer: {ImagePath}", c.ImagePath);
                return new List<string> { c.ImagePath };
            }
        }).ToList();

        var seenUrls = new HashSet<string>(StringComparer.Ordinal);
        var allUniqueImages = chunkImageLists.SelectMany(x => x).Where(p => seenUrls.Add(p)).ToList();
        var sentImageUrls = allUniqueImages.Take(_client.MaxImages).ToList();

        var contextParts = new List<string>();
        for (var ci = 0; ci < chunkList.Count; ci++)
        {
            var c = chunkList[ci];
            var paths = chunkImageLists[ci];

            var cleanContent = System.Text.RegularExpressions.Regex.Replace(
                c.Content.Trim(), @"\[RESIM:[^\]]*\]", "").Trim();

            string chunkText;
            if (paths.Count > 0)
            {
                // [IMG:N — caption] → ![alt](/uploads/path)
                // - alt: inline caption varsa kullanılır (Pixtral üretimi), yoksa "görsel"
                // - src: /uploads/{filename} — static file serving
                var resolvedContent = ImgMarkerRegex.Replace(cleanContent, m =>
                {
                    if (!int.TryParse(m.Groups[1].Value, out var local1Based)) return m.Value;
                    var localIdx = local1Based - 1;
                    if (localIdx < 0 || localIdx >= paths.Count) return m.Value;

                    var path = paths[localIdx];
                    // Group 2 = " — caption" veya boş. Trim '—', '–', '-' ve boşluklar.
                    var captionRaw = m.Groups[2].Value.Trim();
                    var alt = string.IsNullOrEmpty(captionRaw)
                        ? "gorsel"
                        : captionRaw.TrimStart(' ', '—', '–', '-', ':').Trim();
                    // Markdown alt text içinde ] olmamalı, parantez kaçır
                    alt = alt.Replace("[", "(").Replace("]", ")");
                    if (alt.Length > 200) alt = alt[..200].TrimEnd() + "...";

                    return $"![{alt}](/uploads/{path})";
                });

                // LLM'e "bu kaynağın görselleri" ipucu — kullanılabilecek path listesi
                var availableImgs = paths
                    .Select((p, i) => $"![{(i + 1)}](/uploads/{p})")
                    .ToList();
                var imgHint = availableImgs.Count > 0
                    ? $"[BU KAYNAĞIN KULLANILABİLİR GÖRSELLERİ: {string.Join(" ", availableImgs)}]\n\n"
                    : "";

                var pageSuffix = c.PageNumber.HasValue ? $" | Sayfa {c.PageNumber}" : "";
                var headerSuffix = !string.IsNullOrWhiteSpace(c.Header) ? $" | {c.Header}" : "";
                _logger.LogInformation("[LLM Context] KAYNAK [{Index}] | {FileName} | Sayfa {Page} - {Count} markdown img",
                    ci + 1, c.FileName, c.PageNumber?.ToString() ?? "-", paths.Count);
                chunkText = $"═══════════════════════════════════════════════════════════\n" +
                            $"  KAYNAK [{ci + 1}]  •  Belge: {c.FileName}{pageSuffix}{headerSuffix}\n" +
                            $"═══════════════════════════════════════════════════════════\n\n" +
                            imgHint +
                            resolvedContent;
            }
            else
            {
                var pageSuffix = c.PageNumber.HasValue ? $" | Sayfa {c.PageNumber}" : "";
                var headerSuffix = !string.IsNullOrWhiteSpace(c.Header) ? $" | {c.Header}" : "";
                chunkText = $"═══════════════════════════════════════════════════════════\n" +
                            $"  KAYNAK [{ci + 1}]  •  Belge: {c.FileName}{pageSuffix}{headerSuffix}\n" +
                            $"═══════════════════════════════════════════════════════════\n\n" +
                            cleanContent;
            }

            contextParts.Add(chunkText);
        }

        var context = string.Join("\n\n---\n\n", contextParts);
        return (context, sentImageUrls);
    }

    private static IReadOnlyList<(string Role, string Content)> TrimHistory(
        IEnumerable<(string Role, string Content)>? history)
    {
        var list = history?.ToList() ?? new();
        return list.Select(h =>
        {
            var isUser = h.Role.Equals("user", StringComparison.OrdinalIgnoreCase);
            var maxLen = isUser ? 2000 : 3000;
            var content = h.Content.Length > maxLen ? h.Content[..maxLen] + "…" : h.Content;
            return (Role: h.Role.ToLowerInvariant(), Content: content);
        }).ToList();
    }

    public async Task<bool> IsCacheableAsync(
        string question,
        IEnumerable<(string Role, string Content)>? history = null,
        CancellationToken ct = default)
    {
        var historySection = "";
        if (history != null)
        {
            var recent = history.TakeLast(6).ToList();
            if (recent.Count > 0)
            {
                var lines = recent.Select(h =>
                    (h.Role.Equals("user", StringComparison.OrdinalIgnoreCase) ? "Kullanıcı" : "Asistan") + ": " +
                    h.Content[..Math.Min(300, h.Content.Length)]);
                historySection = "SON KONUŞMA (bağlam için):\n" + string.Join("\n", lines) + "\n\n";
            }
        }

        // Helper modele alındı — binary classifier, main rate-limit'i tüketmemeli.
        // JSON çıktı: {"standalone": true/false} → dil-bağımsız, "evet/hayir/yes/no" gibi
        // kelime tahminine gerek yok.
        var payload = HelperPayload(
            LlmPrompts.IsCacheable.System,
            LlmPrompts.IsCacheable.User(question, historySection),
            maxTokens: 20, temperature: 0.0f);

        try
        {
            var response = await _client.PostHelperAsync(payload, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogDebug("[Cache] IsCacheable helper HTTP {S} — varsayılan: evet", (int)response.StatusCode);
                return true;
            }

            var raw = (await ReadContentAsync(response, ct))?.Trim() ?? "";
            if (string.IsNullOrEmpty(raw))
            {
                _logger.LogDebug("[Cache] IsCacheable boş yanıt — varsayılan: evet (yazılabilir)");
                return true;
            }

            // Markdown code fence varsa temizle
            raw = System.Text.RegularExpressions.Regex.Replace(raw, @"^```(?:json)?\s*|\s*```$", "");
            using var doc = JsonDocument.Parse(raw);
            var standalone = doc.RootElement.TryGetProperty("standalone", out var s)
                && s.ValueKind == JsonValueKind.True;
            _logger.LogDebug("[Cache] IsCacheable → standalone={Standalone}", standalone);
            return standalone;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("[Cache] IsCacheable hata: {Message} — varsayılan: evet (yazılabilir)", ex.Message);
            return true;
        }
    }

    public async Task<string?> BuildContextualSearchQueryAsync(
        string question,
        IEnumerable<(string Role, string Content)> history,
        CancellationToken ct = default)
    {
        var recent = history.TakeLast(4).ToList();
        if (recent.Count == 0) return null;

        var historyText = string.Join("\n", recent.Select(h =>
            (h.Role.Equals("user", StringComparison.OrdinalIgnoreCase) ? "Kullanıcı" : "Asistan") + ": " +
            h.Content[..Math.Min(1500, h.Content.Length)]));

        var payload = HelperPayload(
            LlmPrompts.ContextualSearch.System,
            LlmPrompts.ContextualSearch.User(historyText, question),
            maxTokens: 100, temperature: 0.0f);

        try
        {
            var response = await _client.PostHelperAsync(payload, ct);
            if (!response.IsSuccessStatusCode) return null;
            var enriched = await ReadContentAsync(response, ct);
            if (string.IsNullOrWhiteSpace(enriched)) return null;

            _logger.LogInformation("[SearchEnrich] '{Q}' → '{E}'", question, enriched);
            return enriched;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("[SearchEnrich] Hata: {Msg} — ham soruya geri düşülür", ex.Message);
            return null;
        }
    }

    public async Task<List<string>> GenerateFollowUpQuestionsAsync(
        string question,
        string answer,
        IEnumerable<ChunkResult> chunks,
        CancellationToken ct = default)
    {
        // Top 3 chunk × 600 char — daha fazla bağlam → LLM daha çeşitli/derin takip soruları üretir.
        var context = string.Join("\n\n", chunks
            .Take(3)
            .Select((c, i) => $"[{i + 1}] {c.Content[..Math.Min(600, c.Content.Length)].Trim()}"));

        if (string.IsNullOrWhiteSpace(context)) context = answer;
        if (string.IsNullOrWhiteSpace(context)) return new List<string>();

        var payload = HelperPayload(
            LlmPrompts.FollowUp.System,
            LlmPrompts.FollowUp.User(question, answer, context),
            maxTokens: 300, temperature: 0.3f);

        try
        {
            var response = await _client.PostHelperAsync(payload, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("[FollowUp] HTTP {S} — boş liste döner (helper rate-limit olabilir)",
                    (int)response.StatusCode);
                return new List<string>();
            }

            var raw = await ReadContentAsync(response, ct) ?? "";
            if (string.IsNullOrWhiteSpace(raw))
            {
                _logger.LogInformation("[FollowUp] Model boş içerik döndü — öneri yok");
                return new List<string>();
            }

            var options = raw
                .Split('|', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => s.Length > 5 && s.Length < 250)
                .Take(3)
                .ToList();

            _logger.LogInformation("[FollowUp] {Count} öneri üretildi", options.Count);
            return options;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("[FollowUp] Hata: {Message}", ex.Message);
            return new List<string>();
        }
    }

    public async Task<List<string>> GenerateClarificationsAsync(
        string question,
        IEnumerable<(string Role, string Content)> history,
        IEnumerable<string>? availableDocuments = null,
        CancellationToken ct = default)
    {
        var docList = availableDocuments?.ToList() ?? new();
        var docSection = LlmPrompts.Clarification.DocSection(docList);

        var histLines = string.Join("\n", history.TakeLast(4)
            .Select(h => (h.Role.Equals("user", StringComparison.OrdinalIgnoreCase) ? "Kullanıcı" : "Asistan") +
                         ": " + h.Content[..Math.Min(200, h.Content.Length)]));

        try
        {
            // Clarification ana modelde — talimat takibi kritik, helper'ın yan etkileri var.
            var answer = await _client.PostMainTextAsync(
                LlmPrompts.Clarification.System(docSection),
                LlmPrompts.Clarification.User(question, histLines),
                maxTokens: 200, temperature: 0.2f, ct);

            if (string.IsNullOrWhiteSpace(answer)) return new List<string>();

            var options = answer
                .Split('|', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Select(SanitizeFileReferences)  // safety net — LLM yine de sızdırırsa kırp
                .Where(s => s.Length > 5 && s.Length < 250)
                .Take(5)
                .ToList();

            _logger.LogDebug("[Clarify] '{Question}' → {Count} seçenek", question, options.Count);
            return options;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Clarify] Seçenek üretilemedi — soru: {Question}", question);
            return new List<string>();
        }
    }

    // Clarification seçenekleri için safety net — LLM dosya/belge atıflarını yazdıysa temizle.
    // Üç desen: (a) dosya uzantısı içeren tokenlar (".pdf, .xlsx vb.")
    //         (b) "X dosyasındaki|belgesindeki|dökümanında" gibi atıf cümlecikleri
    //         (c) "şu/bu/söz konusu belge/doküman/dosya" ifadeleri
    private static string SanitizeFileReferences(string option)
    {
        if (string.IsNullOrWhiteSpace(option)) return option;
        var s = option;

        // (a) Dosya uzantısı kalıbı: "AdSoyad.pdf" → boşluk (token tamamen düşer)
        s = System.Text.RegularExpressions.Regex.Replace(
            s, @"\b[\wÇĞİÖŞÜçğıöşü\-_]+\.(pdf|docx?|xlsx?|csv|mhtml?|txt|pptx?)\b",
            "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        // (b) "X dosyasındaki/belgesindeki/dökümanındaki/dokümanındaki" → boş
        s = System.Text.RegularExpressions.Regex.Replace(
            s, @"\b\w*\s*(dosya|belge|d[öo]k[üu]man)\w*\s+(yer\s+alan|bulunan|içindeki|bulunduğu|olan)?\s*",
            "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        // (c) "şu/bu/söz konusu/ilgili belge/doküman/dosya" ifadeleri
        s = System.Text.RegularExpressions.Regex.Replace(
            s, @"\b(şu|bu|söz\s+konusu|ilgili)\s+(belge|doküman|d[öo]k[üu]man|dosya)\w*\s*",
            "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        // Çift boşlukları + baştaki/sondaki noktalama temizliği
        s = System.Text.RegularExpressions.Regex.Replace(s, @"\s+", " ").Trim();
        s = s.Trim(' ', ',', '.', ';', ':');

        return s;
    }

    public async Task<string> GenerateChunkContextAsync(
        string documentSummary,
        string? sectionHeader,
        string chunkContent,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(chunkContent)) return string.Empty;

        var payload = HelperPayload(
            LlmPrompts.ChunkContext.System,
            LlmPrompts.ChunkContext.User(documentSummary, sectionHeader, chunkContent),
            maxTokens: 80, temperature: 0.1f);

        try
        {
            var response = await _client.PostHelperAsync(payload, ct);
            if (!response.IsSuccessStatusCode) return string.Empty;
            var ctx = (await ReadContentAsync(response, ct) ?? "").Replace('\n', ' ').Replace('\r', ' ').Trim();
            return ctx;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[ChunkContext] üretilemedi — chunk orijinaliyle indexlenecek");
            return string.Empty;
        }
    }

    public async Task<IReadOnlyList<string>> GenerateChunkContextsBatchAsync(
        string documentSummary,
        IReadOnlyList<(string? Header, string Content)> chunks,
        CancellationToken ct = default)
    {
        if (chunks.Count == 0) return Array.Empty<string>();

        var results = new string[chunks.Count];
        var payload = HelperPayload(
            LlmPrompts.ChunkContextBatch.System,
            LlmPrompts.ChunkContextBatch.User(documentSummary, chunks),
            maxTokens: Math.Min(1500, chunks.Count * 100),
            temperature: 0.1f);

        try
        {
            var response = await _client.PostHelperAsync(payload, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("[BatchCtx] Helper HTTP {S} — fallback empty",
                    (int)response.StatusCode);
                return results;
            }

            var raw = (await ReadContentAsync(response, ct) ?? "").Trim();
            if (string.IsNullOrEmpty(raw)) return results;

            raw = System.Text.RegularExpressions.Regex.Replace(raw, @"^```(?:json)?\s*|\s*```$", "");

            using var doc = JsonDocument.Parse(raw);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return results;

            var idx = 0;
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                if (idx >= chunks.Count) break;
                string? v = null;
                if (item.ValueKind == JsonValueKind.Object
                    && item.TryGetProperty("context", out var ctxEl)
                    && ctxEl.ValueKind == JsonValueKind.String)
                    v = ctxEl.GetString();
                else if (item.ValueKind == JsonValueKind.String)
                    v = item.GetString();

                if (!string.IsNullOrWhiteSpace(v))
                    results[idx] = v.Length > 300 ? v[..300] : v;
                idx++;
            }

            _logger.LogInformation("[BatchCtx] {N} chunk için context üretildi", chunks.Count);
            return results;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[BatchCtx] Hata — boş array");
            return results;
        }
    }

    public async Task<string?> ValidateCachedAnswerAsync(
        string question,
        string cachedQuestion,
        string cachedAnswer,
        IEnumerable<(string Role, string Content)>? history = null,
        string? userFeedbackContext = null,
        CancellationToken ct = default)
    {
        var historySection = "";
        if (history != null)
        {
            var recent = history.TakeLast(6).ToList();
            if (recent.Count > 0)
            {
                var lines = recent.Select(h =>
                    (h.Role.Equals("user", StringComparison.OrdinalIgnoreCase) ? "Kullanıcı" : "Asistan") + ": " +
                    h.Content[..Math.Min(150, h.Content.Length)]);
                historySection = $"SON KONUŞMA:\n{string.Join("\n", lines)}\n\n";
            }
        }

        // Kullanıcının geçmiş şikayetleri varsa system prompt'a iliştir → validate kararı
        // bu uyarıyı dikkate alır (örn. benzer cevaba "yanlış bilgi" dendiyse "valid":false dön).
        var systemPrompt = string.IsNullOrWhiteSpace(userFeedbackContext)
            ? LlmPrompts.CacheValidation.System
            : LlmPrompts.CacheValidation.System + "\n" + userFeedbackContext;

        var payload = HelperPayload(
            systemPrompt,
            LlmPrompts.CacheValidation.User(historySection, question, cachedQuestion, cachedAnswer),
            maxTokens: 20, temperature: 0.0f);

        try
        {
            var response = await _client.PostHelperAsync(payload, ct);
            if (!response.IsSuccessStatusCode)
            {
                // Fail-open: helper rate-limited olduğunda cache hit'i GEÇERLİ say.
                _logger.LogDebug("[CacheValidate] Helper HTTP {S} — fail-open: cache kabul edildi",
                    (int)response.StatusCode);
                return cachedAnswer;
            }

            // JSON çıktı: {"valid": true/false} — dil-bağımsız parse, Türkçe karakter normalize yok.
            var raw = (await ReadContentAsync(response, ct) ?? "").Trim();
            if (string.IsNullOrEmpty(raw))
            {
                _logger.LogDebug("[CacheValidate] Boş yanıt — fail-open: cache kabul edildi");
                return cachedAnswer;
            }

            raw = System.Text.RegularExpressions.Regex.Replace(raw, @"^```(?:json)?\s*|\s*```$", "");
            using var doc = JsonDocument.Parse(raw);
            var isValid = doc.RootElement.TryGetProperty("valid", out var v)
                && v.ValueKind == JsonValueKind.True;
            _logger.LogDebug("[CacheValidate] Soru='{Question}' → valid={Valid}", question, isValid);
            return isValid ? cachedAnswer : null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("[CacheValidate] Hata: {Msg} — fail-open: cache kabul edildi", ex.Message);
            return cachedAnswer;
        }
    }

    public string BuildFeedbackContextPrompt(
        IReadOnlyList<(string Question, string Answer, string? ReasonText, IReadOnlyList<string> Categories)> items)
    {
        return LlmPrompts.FeedbackContext.Build(items);
    }

    public async Task<string?> SummarizeConversationAsync(
        IReadOnlyList<(string Role, string Content)> messages,
        CancellationToken ct = default)
    {
        if (messages.Count == 0) return null;

        // Konuşmayı düz text'e çevir (her mesaj 800 char ile kırpılır — uzun cevap detayını ezme)
        var conversationText = string.Join("\n\n", messages.Select(m =>
        {
            var role = m.Role.Equals("user", StringComparison.OrdinalIgnoreCase) ? "Kullanıcı" : "Asistan";
            var content = m.Content.Length > 800 ? m.Content[..800] + "…" : m.Content;
            return $"{role}: {content}";
        }));

        var payload = HelperPayload(
            LlmPrompts.ConversationSummary.System,
            LlmPrompts.ConversationSummary.User(conversationText),
            maxTokens: 250, temperature: 0.2f);

        try
        {
            var response = await _client.PostHelperAsync(payload, ct);
            if (!response.IsSuccessStatusCode) return null;
            var summary = (await ReadContentAsync(response, ct) ?? "").Trim();
            if (string.IsNullOrWhiteSpace(summary)) return null;
            if (summary.Length > 800) summary = summary[..800];
            _logger.LogInformation("[ConvSummary] {N} mesaj → {Len} char özet", messages.Count, summary.Length);
            return summary;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[ConvSummary] Özet üretilemedi — fallback raw history");
            return null;
        }
    }

    public async Task<string?> GenerateDocumentSummaryAsync(string sampleContent, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(sampleContent)) return null;

        var truncated = sampleContent.Length > 4000 ? sampleContent[..4000] : sampleContent;
        var payload = HelperPayload(
            LlmPrompts.DocumentSummary.System,
            LlmPrompts.DocumentSummary.User(truncated),
            maxTokens: 80, temperature: 0.2f);

        try
        {
            var response = await _client.PostHelperAsync(payload, ct);
            if (!response.IsSuccessStatusCode) return null;
            var summary = (await ReadContentAsync(response, ct) ?? "").Trim('"', '\'');
            if (string.IsNullOrWhiteSpace(summary)) return null;

            if (summary.Length > 200) summary = summary[..200];
            _logger.LogInformation("[DocSummary] Üretildi: {Summary}", summary);
            return summary;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[DocSummary] Üretim başarısız");
            return null;
        }
    }

    public async Task<AnswerQualityResult> ValidateAnswerQualityAsync(
        string question,
        IEnumerable<ChunkResult> chunks,
        string answer,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(answer)) return AnswerQualityResult.Failed("empty_answer");

        // Top 5 chunk × 1000 char yeterli; 8 × 1500 helper'a aşırı yük (12K char → ~3K token).
        var chunkList = chunks.Take(5).ToList();
        var chunksText = string.Join("\n\n", chunkList.Select((c, i) =>
            $"[CHUNK {i + 1} - {c.FileName}]\n{(c.Content.Length > 1000 ? c.Content[..1000] + "..." : c.Content)}"));

        var payload = HelperPayload(
            LlmPrompts.AnswerQuality.System,
            LlmPrompts.AnswerQuality.User(question, chunksText, answer),
            maxTokens: 1000, temperature: 0.0f);

        try
        {
            var response = await _client.PostHelperAsync(payload, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("[AnswerQuality] HTTP {S} — Unvalidated (cache yazılmaz)",
                    (int)response.StatusCode);
                return AnswerQualityResult.Unvalidated();
            }

            var raw = await ReadContentAsync(response, ct);
            if (string.IsNullOrEmpty(raw))
            {
                _logger.LogWarning("[AnswerQuality] LLM boş içerik döndü — Unvalidated");
                return AnswerQualityResult.Unvalidated();
            }

            // Markdown code fence temizliği: ```json {...} ```
            raw = System.Text.RegularExpressions.Regex.Replace(raw, @"^```(?:json)?\s*", "");
            raw = System.Text.RegularExpressions.Regex.Replace(raw, @"\s*```$", "");

            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;

            var score = root.TryGetProperty("score", out var s) && s.ValueKind == JsonValueKind.Number
                ? Math.Clamp(s.GetDouble(), 0.0, 1.0)
                : 1.0;

            var issues = new List<string>();
            if (root.TryGetProperty("issues", out var iss) && iss.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in iss.EnumerateArray())
                {
                    var str = item.GetString();
                    if (!string.IsNullOrWhiteSpace(str)) issues.Add(str);
                }
            }

            _logger.LogInformation("[AnswerQuality] Score={Score}, Issues={Count}", score, issues.Count);
            return new AnswerQualityResult(score, issues);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[AnswerQuality] Parse/çağrı hatası — Unvalidated (cache yazılmaz)");
            return AnswerQualityResult.Unvalidated();
        }
    }

    public async Task<string?> GenerateImageCaptionAsync(
        byte[] imageBytes,
        string mimeType,
        string context,
        CancellationToken ct = default)
    {
        if (imageBytes == null || imageBytes.Length < 64) return null;

        var maxDim = int.TryParse(_cfg["Caption:MaxImageDimension"], out var d) ? d : 1024;
        var skipBelow = int.TryParse(_cfg["Caption:SkipResizeBelow"], out var s) ? s : 800;
        // Resizer hem byte hem effective MIME döner — resize edildiyse her zaman image/jpeg,
        // edilmediyse caller'ın bildirdiği orijinal mime korunur. Manuel magic-byte detection yok.
        var resized = ImageResizer.ResizeIfNeeded(imageBytes, mimeType, maxDim, skipBelow);
        var base64 = Convert.ToBase64String(resized.Bytes);

        var prompt = LlmPrompts.ImageCaption.Build(context);
        var userContent = new object[]
        {
            new { type = "text", text = prompt },
            new { type = "image_url", image_url = new { url = $"data:{resized.MimeType};base64,{base64}" } }
        };

        var payload = new
        {
            model = _client.CaptionModel,
            max_tokens = 150,
            temperature = 0.2f,
            messages = new object[] { new { role = "user", content = userContent } }
        };

        // Retry: 3 deneme, exponential backoff (1s, 2s, 4s). Geçici hatalarda (429, 5xx, ağ)
        // tekrar denenir; 400/401/403 gibi kalıcı hatalarda denenmez.
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            var response = await _client.PostCaptionAsync(payload, ct);
            if (response is null)
            {
                if (attempt < 3)
                {
                    await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt - 1)), ct);
                    continue;
                }
                return null;
            }

            try
            {
                if (!response.IsSuccessStatusCode)
                {
                    var status = (int)response.StatusCode;
                    var isTransient = status == 429 || status >= 500;
                    _logger.LogWarning(
                        "[Caption] {Provider} HTTP {S} (attempt {N}/3) — {Action}",
                        _client.CaptionProviderLabel, status, attempt,
                        isTransient && attempt < 3 ? "retry" : "vazgeç");

                    if (isTransient && attempt < 3)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt - 1)), ct);
                        continue;
                    }
                    return null;
                }

                var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
                var caption = json.GetProperty("choices")[0].GetProperty("message")
                                  .GetProperty("content").GetString()?.Trim().Trim('"', '\'');
                if (string.IsNullOrWhiteSpace(caption)) return null;
                if (caption.Length > 500) caption = caption[..500];
                return caption;
            }
            catch (Exception ex) when (attempt < 3 && !ct.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "[Caption] {Provider} parse hatası (attempt {N}/3) — retry",
                    _client.CaptionProviderLabel, attempt);
                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt - 1)), ct);
            }
            finally
            {
                response.Dispose();
            }
        }
        return null;
    }

    // Helper LLM çağrıları için ortak payload kalıbı.
    private object HelperPayload(string system, string user, int maxTokens, float temperature) => new
    {
        model = _client.HelperModel,
        max_tokens = maxTokens,
        temperature,
        messages = new object[]
        {
            new { role = "system", content = system },
            new { role = "user",   content = user }
        }
    };

    private static async Task<string?> ReadContentAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        return json.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString()?.Trim();
    }
}
