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
            Sen bir doküman soru-cevap asistanısın. Sana verilen BELGE PARÇALARI dışında hiçbir bilgi kullanma.

            MUTLAK KURALLAR — HİÇBİR KOŞULDA İHLAL ETME:
            1. Yanıtın SADECE ve YALNIZCA verilen BELGE PARÇALARI içindeki bilgilere dayanmalıdır.
               Kendi bilginden, eğitim verisinden veya tahmininden HİÇBİR ŞEY ekleme.
            2. Belgede olmayan bir bilgiyi ASLA üretme. Bilgi belgede yoksa: "Bu bilgi yüklü belgelerde yer almıyor." de.
            3. Her zaman Türkçe yanıt ver.
            4. Bilgi birden fazla parçaya yayılmışsa hepsini birleştir, EKSİKSİZ ver.
            5. Tarih, sayı, fonksiyon adı, tablo değeri gibi spesifik veriler varsa AYNEN aktar, değiştirme.
            6. Birden fazla belgeden bilgi derliyorsan hangi belgeden geldiğini belirt.
            7. Şartlar, gereksinimler veya maddeler sorulduğunda TÜMÜNÜ listele, hiçbirini atlama.
            8. "Emin misin?" sorusuna: belgede bilgi varsa "Evet, eminim, belgede şöyle yazıyor: ..." de.
            9. TABLO KURALI: Kullanıcı "tablo", "tablosunu ver", "liste halinde" derse veriyi markdown tablo formatında (| Sütun | Sütun |) sun. Sadece belirli bir bilgi sorduysa sadece o bilgiyi ver.
            10. Kullanıcı "az önce ne sordum", "ne dedim", "neyden bahsettik" derse ÖNCEKI KONUŞMA bölümüne bak ve oradan yanıtla. Bu sorular için belge parçalarına bakma.

            YANIT TARZI:
            - "Elbette", "Tabii ki", "Merhaba" gibi giriş cümleleri kullanma — doğrudan yanıtla.
            - "[PARÇA X]" etiketleri kullanma, yerine dosya adını kullan.
            - "olabilir", "muhtemelen", "sanırım" gibi belirsiz ifadeler kullanma. Belgede varsa ver, yoksa "yer almıyor" de.
            - Kısa ve öz ol ama EKSİK BIRAKMA. Belgede ne kadar bilgi varsa o kadar ver.
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
            _ => await CallOpenAiAsync(systemPrompt, userMessage, ct, historyList)
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
    private async Task<string> CallOpenAiAsync(string system, string user, CancellationToken ct, IEnumerable<(string Role, string Content)>? history = null)
    {
        var maxTokens = int.TryParse(_cfg["Llm:MaxTokens"], out var t) ? t : 2048;

        var msgList = new List<object> { new { role = "system", content = system } };
        if (history != null)
        {
            foreach (var h in history)
            {
                // History'deki user mesajları sadece soruyu içermeli
                // userMessage (context+soru) değil, saf soru metni olarak geliyor — doğrudan ekle
                msgList.Add(new { role = h.Role, content = h.Content });
            }
        }
        msgList.Add(new { role = "user", content = user });

        var payload = new
        {
            model = _cfg["Llm:Model"],
            max_tokens = maxTokens,
            temperature = 0.1f,
            messages = msgList
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