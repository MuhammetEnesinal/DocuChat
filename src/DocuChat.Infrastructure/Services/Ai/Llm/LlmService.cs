using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
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
    private readonly ILogger<LlmService> _logger;

    public LlmService(HttpClient http, IHttpClientFactory httpFactory, IConfiguration cfg, ILogger<LlmService> logger)
    {
        _cfg = cfg;
        _logger = logger;
        _client = new MistralChatClient(http, httpFactory, cfg, logger);
    }

    // Streaming variant — token delta'larını üretir. OpenAI-compat dışındaki provider'lar için
    // (Anthropic/Gemini) tam cevap tek delta olarak döner (fallback). Ollama'nın kendi streaming
    // formatı OpenAI'dan farklı olduğu için onu da non-streaming'e düşürdük.
    public async IAsyncEnumerable<string> AskStreamAsync(
        string question,
        IEnumerable<ChunkResult> contextChunks,
        IEnumerable<(string Role, string Content)>? history = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var chunkList = contextChunks
            .Where(c => !string.IsNullOrWhiteSpace(c.Content) && c.Content.Trim().Length > 20)
            .Take(5)
            .ToList();

        if (chunkList.Count == 0)
        {
            yield return "Sisteme yuklenmis belgeler arasinda bu soruyla ilgili bilgi bulunamadi.";
            yield break;
        }

        var (context, imageUrls) = BuildContextAndImages(chunkList);
        var historyMessages = TrimHistory(history);
        var userMessage = LlmPrompts.Answer.User(context, question);

        // Sadece OpenAI-compat provider'da gerçek streaming yap; diğerlerinde non-streaming sync call
        if (_client.MainProvider is "Anthropic" or "Gemini" or "Ollama")
        {
            var full = _client.MainProvider switch
            {
                "Anthropic" => await _client.CallAnthropicAsync(LlmPrompts.Answer.System, userMessage, historyMessages, ct),
                "Gemini"    => await _client.CallGeminiAsync(LlmPrompts.Answer.System, userMessage, historyMessages, ct),
                _           => await _client.CallOllamaAsync(LlmPrompts.Answer.System, userMessage, ct)
            };
            yield return full;
            yield break;
        }

        await foreach (var delta in _client.CallOpenAiWithVisionStreamingAsync(
            LlmPrompts.Answer.System, userMessage, imageUrls, historyMessages, ct))
        {
            yield return delta;
        }
    }

    // Chunk içeriklerini KAYNAK bloklarına çevirir + global resim listesi + [IMG_REF:n] → [IMG:N] map.
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
        var pathToGlobalIdx = sentImageUrls.Select((p, i) => (p, i)).ToDictionary(x => x.p, x => x.i + 1);

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
                var resolvedContent = System.Text.RegularExpressions.Regex.Replace(
                    cleanContent,
                    @"\[IMG_REF:(\d+)\]",
                    m =>
                    {
                        if (int.TryParse(m.Groups[1].Value, out var localIdx)
                            && localIdx < paths.Count
                            && pathToGlobalIdx.TryGetValue(paths[localIdx], out var gIdx))
                            return $"[IMG:{gIdx}]";
                        return "";
                    });

                if (!resolvedContent.Contains("[IMG:"))
                {
                    var sentFromChunk = paths.Where(p => pathToGlobalIdx.ContainsKey(p)).ToList();
                    if (sentFromChunk.Count > 0)
                    {
                        var nums = string.Join(", ", sentFromChunk.Select(p => $"[IMG:{pathToGlobalIdx[p]}]"));
                        resolvedContent = $"[GORSELLER: {sentFromChunk.Count} adet - {nums}]\n\n{resolvedContent}";
                    }
                }

                var headerSuffix = !string.IsNullOrWhiteSpace(c.Header) ? $" | {c.Header}" : "";
                _logger.LogDebug("[LLM Context] Parca {Index} | {FileName} - {ImageCount} gorsel", ci + 1, c.FileName, paths.Count);
                chunkText = $"═══════════════════════════════════════════════════════════\n" +
                            $"  KAYNAK [{ci + 1}]  •  Belge: {c.FileName}{headerSuffix}\n" +
                            $"═══════════════════════════════════════════════════════════\n\n" +
                            $"{resolvedContent}";
            }
            else
            {
                var headerSuffix = !string.IsNullOrWhiteSpace(c.Header) ? $" | {c.Header}" : "";
                chunkText = $"═══════════════════════════════════════════════════════════\n" +
                            $"  KAYNAK [{ci + 1}]  •  Belge: {c.FileName}{headerSuffix}\n" +
                            $"═══════════════════════════════════════════════════════════\n\n" +
                            $"{cleanContent}";
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

    public async Task<string> GenerateHypotheticalDocumentAsync(string question, CancellationToken ct = default)
    {
        var payload = HelperPayload(LlmPrompts.Hyde.System, LlmPrompts.Hyde.User(question),
            maxTokens: 200, temperature: 0.1f);

        try
        {
            var response = await _client.PostHelperAsync(payload, ct);
            if (!response.IsSuccessStatusCode) return question;
            var content = await ReadContentAsync(response, ct);
            return string.IsNullOrWhiteSpace(content) ? question : content;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[HyDE] Varsayımsal belge üretilemedi — soru: {Question}", question);
            return question;
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
        // Top 2 chunk yeterli — takip sorusu üretmek için cevabın bağlamı kafi.
        var context = string.Join("\n\n", chunks
            .Take(2)
            .Select((c, i) => $"[{i + 1}] {c.Content[..Math.Min(400, c.Content.Length)].Trim()}"));

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

    public async Task<string?> ValidateCachedAnswerAsync(
        string question,
        string cachedQuestion,
        string cachedAnswer,
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
                    h.Content[..Math.Min(150, h.Content.Length)]);
                historySection = $"SON KONUŞMA:\n{string.Join("\n", lines)}\n\n";
            }
        }

        var payload = HelperPayload(
            LlmPrompts.CacheValidation.System,
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
        var resized = ImageResizer.ResizeIfNeeded(imageBytes, maxDim, skipBelow);
        var base64 = Convert.ToBase64String(resized);

        var effectiveMime = resized.Length > 1 && resized[0] == 0xFF && resized[1] == 0xD8
            ? "image/jpeg" : mimeType;

        var prompt = LlmPrompts.ImageCaption.Build(context);
        var userContent = new object[]
        {
            new { type = "text", text = prompt },
            new { type = "image_url", image_url = new { url = $"data:{effectiveMime};base64,{base64}" } }
        };

        var payload = new
        {
            model = _client.CaptionModel,
            max_tokens = 150,
            temperature = 0.2f,
            messages = new object[] { new { role = "user", content = userContent } }
        };

        var response = await _client.PostCaptionAsync(payload, ct);
        if (response is null) return null;
        try
        {
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("[Caption] {Provider} HTTP {S} — null döner",
                    _client.CaptionProviderLabel, (int)response.StatusCode);
                return null;
            }
            var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
            var caption = json.GetProperty("choices")[0].GetProperty("message")
                              .GetProperty("content").GetString()?.Trim().Trim('"', '\'');
            if (string.IsNullOrWhiteSpace(caption)) return null;
            if (caption.Length > 500) caption = caption[..500];
            return caption;
        }
        finally
        {
            response.Dispose();
        }
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
