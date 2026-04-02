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
        CancellationToken ct = default)
    {
        var chunkList = contextChunks
            .Where(c => !string.IsNullOrWhiteSpace(c.Content) && c.Content.Trim().Length > 20)
            .ToList();

        if (chunkList.Count == 0)
            return "Sisteme yüklenmiş belgeler arasında bu soruyla ilgili bilgi bulunamadı.";

        var context = string.Join("\n\n", chunkList.Select((c, i) =>
            $"[PARÇA {i + 1} — {c.FileName}]\n{c.Content.Trim()}"));

        var systemPrompt = """
            Sen bir belge sorgulama motorusun. Sana verilen metin parçaları dışında HİÇBİR bilgiye erişimin yoktur.
            Sanki bu belge parçaları dışında dünyada hiçbir şey bilmiyormuşsun gibi davran.

            KESİN KURALLAR — HİÇBİR İSTİSNASI YOKTUR:

            KURAL 1 — SADECE BELGEDEN CEVAP VER:
            Verilen metin parçalarında bulunmayan hiçbir bilgiyi yanıta ekleme.
            Genel bilgin, eğitim verim, tahmin veya çıkarım yasaktır.
            Parçalarda bilgi yoksa: "Bu bilgi yüklü belgelerde yer almıyor." yaz ve dur.

            KURAL 2 — TÜRKÇE:
            Her zaman Türkçe yanıt ver. İngilizce kelime kullanma.

            KURAL 3 — META İFADE YASAĞI:
            "parça", "belge parçası", "dosyada", "[PARÇA X]" gibi ifadeler kullanma.
            Bilgiyi direkt ver.

            KURAL 4 — BELİRSİZLİK YASAĞI:
            "olabilir", "muhtemelen", "anlaşılabilir", "bağlamda", "yorumlanabilir" gibi
            belirsiz ifadeler kullanma. Belgede varsa ver, yoksa "yer almıyor" de.

            KURAL 5 — EKSİKSİZ AKTAR:
            Belgede ne varsa hepsini eksiksiz ver. Özetleme, atlama.
            Kod, fonksiyon adı, tablo adı, parametre, hata mesajı, madde numarası —
            tüm teknik detayları aynen aktar.

            KURAL 6 — DETAYLI AÇIKLA:
            Madde madde, adım adım açıkla. Tek cümleyle geçme.
            Birden fazla parçada bilgi varsa hepsini birleştir.

            KURAL 7 — GİRİŞ CÜMLESİ YASAĞI:
            "Elbette", "Tabii ki", "Memnuniyetle" gibi giriş cümleleri kullanma.
            Doğrudan yanıtla.
            """;

        var userMessage = $"""
            BELGE PARÇALARI:

            {context}

            ═══════════════════════════════════════════════════════
            SORU: {question}
            ═══════════════════════════════════════════════════════

            TALİMAT:
            Yukarıdaki belge parçalarını tara.
            Soruyla ilgili bilgi varsa — eksiksiz, detaylı, madde madde ver.
            Soruyla ilgili bilgi yoksa — yalnızca "Bu bilgi yüklü belgelerde yer almıyor." yaz.
            Kendi bilginden hiçbir şey ekleme. Sadece belgede ne yazıyorsa onu ver.
            Tablo içeren bilgiler varsa markdown tablo formatında (| Sütun | Sütun |) ver.
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