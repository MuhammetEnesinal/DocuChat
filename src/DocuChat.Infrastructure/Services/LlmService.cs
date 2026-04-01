using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using DocuChat.Application.Abstractions;
using Microsoft.Extensions.Configuration;

namespace DocuChat.Infrastructure.Services;

public class LlmService : ILlmService
{
    private readonly HttpClient _http;
    private readonly IConfiguration _cfg;

    public LlmService(HttpClient http, IConfiguration cfg)
    {
        _http = http;
        _cfg = cfg;

        if (_http.BaseAddress is null && _cfg["Llm:BaseUrl"] is { } url)
            _http.BaseAddress = new Uri(url);

        // Groq / OpenAI icin Bearer token (Anthropic bunu kullanmaz)
        if (_cfg["Llm:ApiKey"] is { Length: > 0 } key &&
            _cfg["Llm:Provider"] is not "Anthropic")
            _http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", key);
    }

    public async Task<string> AskAsync(
        string question,
        IEnumerable<ChunkResult> contextChunks,
        CancellationToken ct = default)
    {
        var chunkList = contextChunks
            .Where(c => !string.IsNullOrWhiteSpace(c.Content) && c.Content.Trim().Length > 20)
            .ToList();

        if (chunkList.Count == 0)
            return "Sisteme yüklenmiş belgeler arasında bu soruyla ilgili bilgi bulunamadı.";

        // Dosya adını context'e ekle — LLM hangi belgeden bilgi aldığını bilir
        var context = string.Join(
            "\n\n",
            chunkList.Select((c, i) =>
                $"[Parça {i + 1}/{chunkList.Count} — Kaynak: {c.FileName}]\n{c.Content.Trim()}")
        );

        var systemPrompt = $"""
            Sen bir doküman soru-cevap asistanısın.
            Görevin: sana verilen belge parçalarını kullanarak kullanıcının sorusunu yanıtlamak.
            Sistemde birden fazla belge olabilir; tüm parçaları değerlendirerek en doğru cevabı ver.

            KESİN KURALLAR:
            1. SADECE aşağıdaki {chunkList.Count} belge parçasındaki bilgileri kullan.
            2. Kendi genel bilginden HİÇBİR ŞEY ekleme.
            3. Her zaman Türkçe yanıt ver.
            4. Bilgi birden fazla parçaya dağılmışsa birleştirerek bütünleşik cevap oluştur.
            5. Sayısal değerler (gün, süre, tarih, yüzde vb.) varsa mutlaka belirt.
            6. Hangi belgeden bilgi aldığını gerekirse belirt (Kaynak: dosya adı).
            7. Cevap gerçekten hiçbir parçada yoksa: "Bu bilgi yüklü belgelerde yer almıyor." de.

            YANIT FORMATI:
            - Gereksiz giriş cümlesi yok ("Elbette", "Tabii ki" vb. kullanma).
            - Doğrudan soruyu yanıtla.
            - Gerekiyorsa madde madde listele.
            - Kısa ve net ol, tekrar yapma.
            """;

        var userMessage = $"""
            BELGE PARÇALARI ({chunkList.Count} adet):

            {context}

            ───────────────────────────────
            SORU: {question}
            ───────────────────────────────

            Talimat:
            - Tüm parçaları tara; cevabı tek parçada bulamazsan parçaları birleştir.
            - Sayısal değer (gün, süre, tarih) geçiyorsa mutlaka yaz.
            - Cevap parçalarda varsa asla "Bu bilgi yüklü belgelerde yer almıyor." deme.
            """;

        return _cfg["Llm:Provider"] switch
        {
            "Anthropic" => await CallAnthropicAsync(systemPrompt, userMessage, ct),
            "Gemini" => await CallGeminiAsync(systemPrompt, userMessage, ct),
            "Ollama" => await CallOllamaAsync(systemPrompt, userMessage, ct),
            _ => await CallOpenAiAsync(systemPrompt, userMessage, ct)
        };
    }

    // ── Anthropic ────────────────────────────────────────────────────────
    private async Task<string> CallAnthropicAsync(string system, string user, CancellationToken ct)
    {
        var maxTokens = int.TryParse(_cfg["Llm:MaxTokens"], out var t) ? t : 2048;

        using var request = new HttpRequestMessage(HttpMethod.Post, string.Empty);
        request.Headers.Add("x-api-key", _cfg["Llm:ApiKey"]);
        request.Headers.Add("anthropic-version", "2023-06-01");
        request.Content = JsonContent.Create(new
        {
            model = _cfg["Llm:Model"] ?? "claude-haiku-4-5-20251001",
            max_tokens = maxTokens,
            system,
            messages = new[] { new { role = "user", content = user } }
        });

        var response = await _http.SendAsync(request, ct);
        await EnsureSuccessAsync(response);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        return json.GetProperty("content")[0].GetProperty("text").GetString()!.Trim();
    }

    // ── Gemini ───────────────────────────────────────────────────────────
    private async Task<string> CallGeminiAsync(string system, string user, CancellationToken ct)
    {
        var apiKey = _cfg["Llm:ApiKey"];
        var model = _cfg["Llm:Model"] ?? "gemini-2.0-flash-001";
        var maxTokens = int.TryParse(_cfg["Llm:MaxTokens"], out var t) ? t : 2048;
        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={apiKey}";

        var payload = new
        {
            system_instruction = new { parts = new[] { new { text = system } } },
            contents = new[] { new { role = "user", parts = new[] { new { text = user } } } },
            generationConfig = new { maxOutputTokens = maxTokens, temperature = 0.2 }
        };

        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        var response = await client.PostAsJsonAsync(url, payload, ct);
        await EnsureSuccessAsync(response);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        return json
            .GetProperty("candidates")[0]
            .GetProperty("content")
            .GetProperty("parts")[0]
            .GetProperty("text")
            .GetString()!.Trim();
    }

    // ── Ollama ───────────────────────────────────────────────────────────
    private async Task<string> CallOllamaAsync(string system, string user, CancellationToken ct)
    {
        var payload = new
        {
            model = _cfg["Llm:Model"],
            stream = false,
            messages = new[]
            {
                new { role = "system", content = system },
                new { role = "user",   content = user   }
            }
        };

        var response = await _http.PostAsJsonAsync("/api/chat", payload, ct);
        await EnsureSuccessAsync(response);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        return json.GetProperty("message").GetProperty("content").GetString()!.Trim();
    }

    // ── OpenAI uyumlu (Groq dahil) ───────────────────────────────────────
    private async Task<string> CallOpenAiAsync(string system, string user, CancellationToken ct)
    {
        var maxTokens = int.TryParse(_cfg["Llm:MaxTokens"], out var t) ? t : 2048;

        var payload = new
        {
            model = _cfg["Llm:Model"],
            max_tokens = maxTokens,
            messages = new[]
            {
                new { role = "system", content = system },
                new { role = "user",   content = user   }
            }
        };

        var response = await _http.PostAsJsonAsync("/openai/v1/chat/completions", payload, ct);
        await EnsureSuccessAsync(response);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        return json
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString()!.Trim();
    }

    // ── Hata yönetimi ────────────────────────────────────────────────────
    private static async Task EnsureSuccessAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return;
        var body = await response.Content.ReadAsStringAsync();
        throw new HttpRequestException(
            $"LLM API hatası [{(int)response.StatusCode}]: {body}",
            inner: null, statusCode: response.StatusCode);
    }
}