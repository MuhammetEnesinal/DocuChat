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

        // Anlamsız / çok kısa soru — belgeyle ilgisi yok, LLM'e gönderme
        var trimmedQ = question.Trim().ToLowerInvariant();
        var meaninglessWords = new HashSet<string>
        {
            "iyi", "aq", "ok", "tamam", "tamamdır", "güzel", "çok güzel",
            "süper", "harika", "teşekkür", "teşekkürler", "sağol", "eyw",
            "anladım", "oldu", "peki", "neyse", "hmm", "hm", "evet", "hayır",
            "yes", "no", "thanks", "thank you", "cool", "nice", "good"
        };
        if (trimmedQ.Length < 4 || meaninglessWords.Contains(trimmedQ))
            return "Belgeler hakkında bir soru sorabilirsiniz.";

        var context = string.Join("\n\n", chunkList.Select(c =>
            $"[KAYNAK: {c.FileName}]\n{c.Content.Trim().Replace("**", "").Replace("---", "")}"));

        var historyList = history?.ToList() ?? new List<(string Role, string Content)>();

        var systemPrompt = """
            Sen kurumsal belgeleri analiz eden, ileri düzey bir doküman asistanısın.
            Görevin: Kullanıcının sorusunu yalnızca sana sunulan BELGE PARÇALARI'na dayanarak eksiksiz ve doğru yanıtlamak.

            ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
            ■ TEMEL KURALLAR
            ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
            1. YALNIZCA verilen belge parçalarındaki bilgiyi kullan. Dışarıdan bilgi üretme.
            2. Bilgi belgede yoksa: "Bu bilgi yüklü belgelerde yer almıyor." de ve dur.
            3. Yanıtını her zaman TÜRKÇE ver.
            4. Cevap birden fazla parçaya yayılmışsa TÜMÜNÜ tara, hepsini birleştir — hiçbir satırı atlama.
            5. Sayı, tarih, kod, oran, ölçü gibi spesifik veriler varsa birebir aktar, yuvarlama.
            6. Maddeler, şartlar veya gereksinimler listesiyse TAMAMINI ver, kısaltma.
            7. Bilgi birden fazla dosyadan geliyorsa her bilginin yanına dosya adını yaz (parantez içinde).

            ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
            ■ TABLO VE LİSTE KURALLARI
            ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
            - Belge parçasında [TABLO BAŞLANGIÇ]...[TABLO BİTİŞ] bloğu varsa içindeki veriyi
              markdown tablo formatında (| Sütun | Sütun |) sun. Tek satır bile atlama.
            - Belge parçasında tablo düz metin olarak geliyorsa (örn: "No: 1, Tanım: ..., İşlem: ...")
              bu veriyi otomatik olarak markdown tabloya dönüştür.
            - Kullanıcı belirli bir tabloyu istiyorsa (örn: "rubrik tablosu", "planlama tablosu")
              YALNIZCA o tabloyu ver, ilgisiz tabloları dahil etme.
            - Kullanıcı "tüm tablo", "tam liste" diyorsa hiçbir satırı atlama.

            ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
            ■ ÇAPRAZ SORGULAMA (Birden fazla belge)
            ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
            - Kullanıcı iki farklı belgeden ilişkili veri soruyorsa (örn: "falçata kullanımında hangi ekipman"):
              → Her iki belgedeki ilgili satırları eşleştir.
              → Eşleşen bilgileri birleştirerek yanıtla.
              → Hangi bilginin hangi dosyadan geldiğini parantez içinde belirt.
            - Aynı konuda birden fazla belgede bilgi varsa hepsini karşılaştırmalı sun.

            ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
            ■ KONUŞMA BAĞLAMI
            ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
            - Kullanıcı "az önce ne sordum", "ne dedim", "önceki soruya göre" derse
              ÖNCEKİ KONUŞMA geçmişine bak ve oradan yanıtla.
            - Kullanıcı "devam et", "bir öncekiyle ilgili" derse bağlamı koru.
            - Her soru bağımsız değil — kullanıcı önceki yanıt üzerine soru sorabilir.

            ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
            ■ YANIT BİÇİMİ
            ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
            - "Elbette", "Tabii ki", "Merhaba", "Size yardımcı olabilirim" gibi giriş cümleleri KULLANMA.
              Doğrudan yanıtla.
            - "[PARÇA X]" gibi iç etiketleri yanıtta gösterme — yerine dosya adını kullan.
            - "olabilir", "muhtemelen", "sanırım", "tahmin ediyorum" gibi belirsiz ifadeler kullanma.
            - Kısa ve öz ol ama EKSİK BIRAKMA. Gerekiyorsa uzun yaz, satır atlama.
            - Kaynak gösterirken sadece dosya adını kullan (parantez içinde), başka etiket ekleme.
            """;

        var userMessage = $"""
            ── BELGE PARÇALARI ──────────────────────────────────────────────────────
            {context}
            ─────────────────────────────────────────────────────────────────────────

            SORU: {question}

            TALİMAT:
            1. Yukarıdaki TÜM parçaları tara — cevap birden fazla parçaya yayılmış olabilir.
            2. İlgili parçaları bulduktan sonra bilgileri eksiksiz birleştirerek yanıtla.
            3. Tablo içeren parçalarda HER SATIRI oku, hiç satır atlama.
            4. Bilgi parçalarda mevcutsa "yer almıyor" yazma — doğrudan yanıtla.
            5. Tablolar için markdown format kullan (| Başlık | Başlık |).
            """;


        return _cfg["Llm:Provider"] switch
        {
            "Anthropic" => await CallAnthropicAsync(systemPrompt, userMessage, ct),
            "Gemini" => await CallGeminiAsync(systemPrompt, userMessage, ct),
            "Ollama" => await CallOllamaAsync(systemPrompt, userMessage, ct),
            _ => await CallOpenAiAsync(systemPrompt, userMessage, ct, historyList)
        };
    }

    // ── Anthropic ─────────────────────────────────────────────────────────
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

    // ── Gemini ────────────────────────────────────────────────────────────
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

    // ── Ollama ────────────────────────────────────────────────────────────
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

    // ── OpenAI uyumlu (Groq dahil) ────────────────────────────────────────
    private async Task<string> CallOpenAiAsync(
        string system, string user, CancellationToken ct,
        IEnumerable<(string Role, string Content)>? history = null)
    {
        var maxTokens = int.TryParse(_cfg["Llm:MaxTokens"], out var t) ? t : 2048;

        var msgList = new List<object> { new { role = "system", content = system } };
        if (history != null)
            foreach (var h in history)
                msgList.Add(new { role = h.Role, content = h.Content });
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

    // ── Hata yönetimi ─────────────────────────────────────────────────────
    private static async Task EnsureSuccessAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return;
        var body = await response.Content.ReadAsStringAsync();
        throw new HttpRequestException(
            $"LLM API hatası [{(int)response.StatusCode}]: {body}",
            inner: null, statusCode: response.StatusCode);
    }
}