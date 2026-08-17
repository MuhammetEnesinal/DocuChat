using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using DocuChat.Application.Common.Imaging;
using DocuChat.Application.Interfaces.Services.Ai.Embedding;
using DocuChat.Application.Interfaces.Services.Ai.Llm;
using DocuChat.Application.Interfaces.Services.Ai.Reranker;
using DocuChat.Application.Interfaces.Services.Ai.Retrieval;
using DocuChat.Application.Interfaces.Services.Documents;
using DocuChat.Application.Interfaces.Services.Auth;
using DocuChat.Application.Interfaces.Services.UserManagement;
using DocuChat.Application.Interfaces.Services.Email;
using DocuChat.Application.Interfaces.Services.Storage;
using DocuChat.Application.Interfaces.Services.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace DocuChat.Infrastructure.Services.Ai.Llm.Http;

// LLM HTTP plumbing — provider çağrıları, retry/budget, helper client yönetimi.
// LlmService bunu pure orchestrator olarak kullanır.
internal sealed class MistralChatClient
{
    private readonly HttpClient _mainHttp;
    private readonly IHttpClientFactory _httpFactory;
    private readonly IConfiguration _cfg;
    private readonly IFileStorage _fileStorage;
    private readonly ILogger _logger;

    public string MainProvider { get; }
    public string MainModel { get; }
    public string MainApiKey { get; }
    public string MainChatPath { get; }
    public int MainMaxTokens { get; }
    public int MaxImages { get; }

    public string HelperBaseUrl { get; }
    public string HelperModel { get; }
    public string HelperApiKey { get; }
    public string HelperChatPath { get; }

    public MistralChatClient(
        HttpClient mainHttp,
        IHttpClientFactory httpFactory,
        IConfiguration cfg,
        IFileStorage fileStorage,
        ILogger logger)
    {
        _mainHttp = mainHttp;
        _httpFactory = httpFactory;
        _cfg = cfg;
        _fileStorage = fileStorage;
        _logger = logger;

        MainProvider = cfg["Llm:Provider"] ?? "OpenAI";
        MainModel = cfg["Llm:Model"] ?? "gpt-4o";
        MainApiKey = cfg["Llm:ApiKey"] ?? string.Empty;
        MainChatPath = cfg["Llm:ChatCompletionsPath"] ?? "/openai/v1/chat/completions";
        MainMaxTokens = int.TryParse(cfg["Llm:MaxTokens"], out var t) ? t : 4096;
        MaxImages = int.TryParse(cfg["Llm:MaxImages"], out var mi) ? mi : 16;

        HelperBaseUrl = cfg["LlmHelper:BaseUrl"] ?? cfg["Llm:BaseUrl"] ?? "https://api.groq.com";
        HelperModel = cfg["LlmHelper:Model"] ?? MainModel;
        HelperApiKey = cfg["LlmHelper:ApiKey"] ?? MainApiKey;
        HelperChatPath = cfg["LlmHelper:ChatCompletionsPath"] ?? "/openai/v1/chat/completions";
    }

    private HttpClient CreateHelperClient()
    {
        var client = _httpFactory.CreateClient();
        client.BaseAddress = new Uri(HelperBaseUrl);
        client.Timeout = TimeSpan.FromMinutes(2);
        if (!string.IsNullOrEmpty(HelperApiKey))
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", HelperApiKey);
        return client;
    }

    // Helper LLM çağrısı — 2 deneme, 8s toplam bütçe. Bütçe aşılırsa cancel exception fırlatır.
    public async Task<HttpResponseMessage> PostHelperAsync(object payload, CancellationToken ct)
    {
        const int maxAttempts = 2;
        var totalBudget = TimeSpan.FromSeconds(8);

        using var budgetCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budgetCts.CancelAfter(totalBudget);
        var linkedCt = budgetCts.Token;

        HttpResponseMessage? lastResp = null;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            var client = CreateHelperClient();
            try
            {
                lastResp = await client.PostAsJsonAsync(HelperChatPath, payload, linkedCt);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                _logger.LogDebug("[Helper] Retry bütçesi ({Sec}s) aşıldı", totalBudget.TotalSeconds);
                throw;
            }
            catch (Exception ex) when (attempt < maxAttempts && !linkedCt.IsCancellationRequested)
            {
                _logger.LogDebug("[Helper] {Type}: {Msg} retry {N}/{M}",
                    ex.GetType().Name, ex.Message, attempt, maxAttempts);
                await Task.Delay(TimeSpan.FromMilliseconds(500), linkedCt);
                continue;
            }

            var status = (int)lastResp.StatusCode;
            var retryable = status == 429 || status >= 500;
            if (!retryable || attempt == maxAttempts) return lastResp;

            var delay = lastResp.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(1);
            if (delay > TimeSpan.FromSeconds(2)) delay = TimeSpan.FromSeconds(2);

            _logger.LogDebug("[Helper] HTTP {S} retry {N}/{M} after {Sec}s",
                status, attempt, maxAttempts, delay.TotalSeconds);
            lastResp.Dispose();

            try { await Task.Delay(delay, linkedCt); }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { break; }
        }

        return lastResp!;
    }

    // Ana model (mistral-large) — text-only çağrı, 20s bütçe, retry yok.
    public async Task<string?> PostMainTextAsync(
        string system, string user, int maxTokens, float temperature, CancellationToken ct)
    {
        var payload = new
        {
            model = MainModel,
            max_tokens = maxTokens,
            temperature,
            messages = new object[]
            {
                new { role = "system", content = system },
                new { role = "user",   content = user }
            }
        };

        try
        {
            using var budgetCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            budgetCts.CancelAfter(TimeSpan.FromSeconds(20));
            var response = await _mainHttp.PostAsJsonAsync(MainChatPath, payload, budgetCts.Token);
            if (!response.IsSuccessStatusCode) return null;

            var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
            return json.GetProperty("choices")[0].GetProperty("message")
                       .GetProperty("content").GetString()?.Trim();
        }
        catch (Exception ex)
        {
            _logger.LogWarning("[MainText] Çağrı hatası: {Msg}", ex.Message);
            return null;
        }
    }

    // OpenAI-compatible streaming (Mistral, Groq, vb.). stream:true ile token bazlı akış.
    // Yield her token'ı geldiğinde anında dışarı verir; HTTP body buffer'lanmaz.
    public async IAsyncEnumerable<string> CallOpenAiWithVisionStreamingAsync(
        string system, string user, IReadOnlyList<string> imagePaths,
        IReadOnlyList<(string Role, string Content)> historyMessages,
        [EnumeratorCancellation] CancellationToken ct)
    {
        // Payload kurulumu (vision dahil) — non-streaming variant ile aynı mantık
        var userContent = new List<object> { new { type = "text", text = user } };

        var cappedUrls = imagePaths.Take(MaxImages).ToList();
        if (imagePaths.Count > MaxImages)
            _logger.LogWarning("[LLM] Resim limiti asildi: {Count} → {Max}'e kirpildi.",
                imagePaths.Count, MaxImages);

        foreach (var imgPath in cappedUrls)
        {
            try
            {
                // İçeriği IFileStorage (S3/MinIO) üzerinden okur.
                byte[] imgBytes;
                using (var imgStream = _fileStorage.OpenRead(imgPath))
                using (var ms = new MemoryStream())
                {
                    await imgStream.CopyToAsync(ms, ct);
                    imgBytes = ms.ToArray();
                }
                // MIME gerçek içerikten (magic bytes) — uzantıya güvenilmez.
                var mime = ImageMagicBytes.DetectMimeType(imgBytes);
                var base64 = Convert.ToBase64String(imgBytes);
                userContent.Add(new { type = "image_url", image_url = new { url = $"data:{mime};base64,{base64}" } });
            }
            catch (Exception ex) { _logger.LogWarning("[LLM] Resim eklenemedi ({Path}): {Message}", imgPath, ex.Message); }
        }

        var msgList = new List<object> { new { role = "system", content = system } };
        foreach (var h in historyMessages)
            msgList.Add(new { role = h.Role, content = h.Content });
        msgList.Add(userContent.Count > 1
            ? (object)new { role = "user", content = userContent }
            : (object)new { role = "user", content = user });

        var payload = new
        {
            model = MainModel,
            max_tokens = MainMaxTokens,
            temperature = 0.05f,
            messages = msgList,
            stream = true
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, MainChatPath)
        {
            Content = JsonContent.Create(payload)
        };

        // ResponseHeadersRead: body'yi tamamlanmadan oku — streaming için kritik
        using var response = await _mainHttp.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException(
                $"LLM stream HTTP [{(int)response.StatusCode}]: {body}",
                inner: null, statusCode: response.StatusCode);
        }

        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        _logger.LogInformation("[LLM Stream] Connection opened — reading SSE stream");
        var tokenCount = 0;

        // SSE format: her event "data: <json>\n\n" ile gelir; [DONE] sonlandırır.
        // EndOfStream property'si bazen blocking — ReadLineAsync null check daha güvenilir.
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            string? line;
            try { line = await reader.ReadLineAsync(ct); }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[LLM Stream] ReadLine hatası");
                break;
            }

            if (line is null) { _logger.LogInformation("[LLM Stream] Stream sonu — toplam {N} token", tokenCount); break; }
            if (string.IsNullOrEmpty(line)) continue;
            if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;

            var dataStr = line[5..].TrimStart();
            if (dataStr == "[DONE]")
            {
                _logger.LogInformation("[LLM Stream] [DONE] alındı — toplam {N} token", tokenCount);
                yield break;
            }

            string? delta = null;
            try
            {
                using var doc = JsonDocument.Parse(dataStr);
                var choices = doc.RootElement.GetProperty("choices");
                if (choices.GetArrayLength() == 0) continue;
                var deltaEl = choices[0].GetProperty("delta");
                if (deltaEl.TryGetProperty("content", out var contentEl)
                    && contentEl.ValueKind == JsonValueKind.String)
                {
                    delta = contentEl.GetString();
                }
            }
            catch (JsonException ex)
            {
                _logger.LogDebug(ex, "[LLM Stream] Malformed event atlandı: {Line}",
                    dataStr.Length > 100 ? dataStr[..100] : dataStr);
                continue;
            }

            if (!string.IsNullOrEmpty(delta))
            {
                tokenCount++;
                yield return delta;
            }
        }
    }

    public async Task<string> CallAnthropicAsync(
        string system, string user,
        IReadOnlyList<(string Role, string Content)> historyMessages,
        CancellationToken ct)
    {
        var msgList = new List<object>();
        foreach (var h in historyMessages)
            msgList.Add(new { role = h.Role, content = h.Content });
        msgList.Add(new { role = "user", content = user });

        using var request = new HttpRequestMessage(HttpMethod.Post, string.Empty);
        request.Content = JsonContent.Create(new
        {
            model = MainModel,
            max_tokens = MainMaxTokens,
            system,
            messages = msgList
        });
        var response = await _mainHttp.SendAsync(request, ct);
        await EnsureSuccessAsync(response);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        return json.GetProperty("content")[0].GetProperty("text").GetString()!.Trim();
    }

    public async Task<string> CallGeminiAsync(
        string system, string user,
        IReadOnlyList<(string Role, string Content)> historyMessages,
        CancellationToken ct)
    {
        var contents = new List<object>();
        foreach (var h in historyMessages)
        {
            var geminiRole = h.Role.Equals("assistant", StringComparison.OrdinalIgnoreCase) ? "model" : "user";
            contents.Add(new { role = geminiRole, parts = new[] { new { text = h.Content } } });
        }
        contents.Add(new { role = "user", parts = new[] { new { text = user } } });

        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{MainModel}:generateContent?key={MainApiKey}";
        var payload = new
        {
            system_instruction = new { parts = new[] { new { text = system } } },
            contents,
            generationConfig = new { maxOutputTokens = MainMaxTokens, temperature = 0.1 }
        };

        using var client = _httpFactory.CreateClient();
        client.Timeout = TimeSpan.FromMinutes(5);
        var response = await client.PostAsJsonAsync(url, payload, ct);
        await EnsureSuccessAsync(response);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        return json.GetProperty("candidates")[0]
                   .GetProperty("content")
                   .GetProperty("parts")[0]
                   .GetProperty("text")
                   .GetString()!.Trim();
    }

    public async Task<string> CallOllamaAsync(string system, string user, CancellationToken ct)
    {
        var payload = new
        {
            model = MainModel,
            stream = false,
            options = new { temperature = 0.1, num_predict = MainMaxTokens },
            messages = new[]
            {
                new { role = "system", content = system },
                new { role = "user",   content = user }
            }
        };
        var response = await _mainHttp.PostAsJsonAsync("/api/chat", payload, ct);
        await EnsureSuccessAsync(response);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        return json.GetProperty("message").GetProperty("content").GetString()!.Trim();
    }

    // Caption kendi API/model'ini kullanabilir; sağlanmazsa ana Llm config'ine düşer.
    public async Task<HttpResponseMessage?> PostCaptionAsync(object payload, CancellationToken ct)
    {
        var captionApiKey = _cfg["Caption:ApiKey"];
        var useCustom = !string.IsNullOrWhiteSpace(captionApiKey);

        var baseUrl = useCustom ? _cfg["Caption:BaseUrl"]! : _cfg["Llm:BaseUrl"]!;
        var path = useCustom ? _cfg["Caption:ChatCompletionsPath"]! : MainChatPath;
        var apiKey = useCustom ? captionApiKey! : MainApiKey;
        var timeoutSec = int.TryParse(_cfg["Caption:TimeoutSeconds"], out var t) ? t : 60;

        var http = _httpFactory.CreateClient();
        http.BaseAddress = new Uri(baseUrl);
        http.Timeout = TimeSpan.FromSeconds(timeoutSec);
        if (!string.IsNullOrEmpty(apiKey))
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        try
        {
            return await http.PostAsJsonAsync(path, payload, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Caption] HTTP hatası");
            return null;
        }
    }

    public string CaptionModel => _cfg["Caption:Model"] ?? MainModel;
    public string CaptionProviderLabel => _cfg["Caption:Provider"]
        ?? (!string.IsNullOrWhiteSpace(_cfg["Caption:ApiKey"]) ? "Custom" : "Main");

    private static async Task EnsureSuccessAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return;
        var body = await response.Content.ReadAsStringAsync();
        throw new HttpRequestException(
            $"LLM API hatasi [{(int)response.StatusCode}]: {body}",
            inner: null, statusCode: response.StatusCode);
    }
}
