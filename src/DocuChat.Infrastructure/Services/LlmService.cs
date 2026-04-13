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

        if (_cfg["Llm:ApiKey"] is { Length: > 0 } key &&
            _cfg["Llm:Provider"] is not "Anthropic")
            _http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", key);
    }

    public async Task<string> AskAsync(
        string question,
        IEnumerable<ChunkResult> contextChunks,
        IEnumerable<(string Role, string Content)>? history = null,
        CancellationToken ct = default)
    {
        var chunkList = contextChunks
            .Where(c => !string.IsNullOrWhiteSpace(c.Content) && c.Content.Trim().Length > 20)
            .ToList();

        if (chunkList.Count == 0)
            return "Sisteme yüklenmiş belgeler arasında bu soruyla ilgili bilgi bulunamadı.";

        var context = string.Join("\n\n", chunkList.Select((c, i) =>
            $"[PARÇA {i + 1} — {c.FileName}]\n{c.Content.Trim()}"));

        // Önceki konuşma geçmişini formatla
        var historyList = history?.ToList() ?? new List<(string Role, string Content)>();
        var historyText = historyList.Count > 0
            ? "\n\nÖNCEKİ KONUŞMA:\n" + string.Join("\n", historyList.Select(h =>
                $"{(h.Role == "user" ? "Kullanıcı" : "Asistan")}: {h.Content}"))
            : string.Empty;

        var systemPrompt = $"""
            Sen kurumsal bir doküman analiz asistanısın. Görevin, sana verilen belge parçalarını
            analiz ederek kullanıcının sorusuna doğrudan, net ve eksiksiz bir yanıt üretmektir.

            TEMEL KURALLAR:
            1. Yanıtın YALNIZCA verilen belge parçalarındaki bilgilere dayanmalıdır.
            2. Her zaman Türkçe yanıt ver.
            3. Bilgi birden fazla parçaya yayılmışsa hepsini birleştirerek tek kapsamlı yanıt oluştur.
            4. Tarih, sayı, süre, yüzde, isim gibi spesifik veriler varsa mutlaka yanıta dahil et.
            5. Madde numaraları veya yönetmelik atıfları varsa aynen aktar.
            6. Birden fazla belgeden bilgi derliyorsan hangi belgeden aldığını dosya adıyla belirt.
            7. Bilgi gerçekten hiçbir parçada yoksa: "Bu bilgi yüklü belgelerde yer almıyor." yaz.
            8. Şartlar, gereksinimler veya belgeler sorulduğunda tüm maddeleri eksiksiz listele, hiçbirini atlama.

            YANIT TARZI:
            - "Elbette", "Tabii ki" gibi gereksiz giriş cümleleri kullanma — doğrudan yanıtla.
            - "[PARÇA X]", "[BELGE PARÇASI X]" gibi iç referans etiketleri KULLANMA.
              Bunun yerine dosya adını kullan: "Etik Kurul Formu'na göre..." gibi.
            - "olabilir", "muhtemelen", "açıkça belirtilmemiştir" gibi belirsiz ifadeler kullanma.
              Belgede bir bilgi varsa o bilgiyi ver, yoksa "yer almıyor" de.
            - Kısa ve öz ol, gereksiz tekrar yapma.
            - Gerektiğinde madde listesi veya tablo kullan.

            ANALİZ STRATEJİSİ:
            - Tüm parçaları tara, soruyla ilgili her bilgiyi topla.
            - Parçalar arasındaki bağlantıları kur.
            - Çelişen bilgiler varsa her ikisini belirt ve farkı açıkla.
            - Kısmi bilgi varsa "Belgede yalnızca şu kadarı belirtilmiştir: ..." şeklinde aktar.
            """;

        var userMessage = $"""
            AŞAĞIDAKİ BELGE PARÇALARINI DİKKATLİCE İNCELE:

            {context}

            ═══════════════════════════════════════
            SORU: {question}
            ═══════════════════════════════════════

            ÖNEMLI:
            • Tüm parçaları tara — cevap farklı parçalara yayılmış olabilir.
            • Tarih, isim, sayı, süre gibi spesifik bilgiler varsa direkt ver.
            • "[PARÇA X]" gibi etiket kullanma, dosya adını kullan.
            • Bilgi parçalarda varsa "yer almıyor" yazma — direkt yanıtla.
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
            generationConfig = new { maxOutputTokens = maxTokens, temperature = 0.1 }
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
            options = new { temperature = 0.1, num_predict = 2048 },
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
            temperature = 0.1f,
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