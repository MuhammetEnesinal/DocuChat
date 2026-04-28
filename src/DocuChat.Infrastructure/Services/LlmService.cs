using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using DocuChat.Application.Interfaces.Services;
using DocuChat.Application.Interfaces.Repositories;
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

        var totalImageCount = 0;
        var context = string.Join("\n\n---\n\n", chunkList.Select((c, i) =>
        {
            var cleanContent = System.Text.RegularExpressions.Regex.Replace(
                c.Content.Trim(), @"\[RESIM:[^\]]*\]", "").Trim();

            if (!string.IsNullOrWhiteSpace(c.ImagePath))
            {
                List<string>? paths = null;
                try { paths = JsonSerializer.Deserialize<List<string>>(c.ImagePath); } catch { }
                var count = paths?.Count ?? 1;
                var nums = string.Join(", ", Enumerable.Range(totalImageCount + 1, count).Select(n => $"[IMG:{n}]"));
                Console.WriteLine($"[LLM Context] Parça {i + 1} - {count} görsel: {nums}");
                totalImageCount += count;
                return $"[PARÇA {i + 1} | Kaynak: {c.FileName}]\n[GÖRSELLER: {count} adet görsel → {nums}]\n\n{cleanContent}";
            }
            return $"[PARÇA {i + 1} | Kaynak: {c.FileName}]\n\n{cleanContent}";
        }));

        var imageUrls = chunkList
            .Where(c => !string.IsNullOrWhiteSpace(c.ImagePath))
            .SelectMany(c =>
            {
                try
                {
                    var parsed = JsonSerializer.Deserialize<List<string>>(c.ImagePath!);
                    return parsed ?? new List<string> { c.ImagePath! };
                }
                catch { return new List<string> { c.ImagePath! }; }
            })
            .Distinct()
            .Take(5)
            .ToList();

        var historyList = history?.ToList() ?? new List<(string Role, string Content)>();

        // Soru validasyonu kaldırıldı — çok agresif filtreliyordu

        var systemPrompt = """
            Sen kurumsal belge analizi konusunda uzmanlaşmış, ileri düzey bir yapay zeka asistanısın.
            Görevin kullanıcının sorusunu derin biçimde analiz etmek, sorunun tam olarak ne istediğini anlamak
            ve YALNIZCA sana verilen belge parçalarından doğru ve isabetli cevap üretmektir.

            ════════════════════════════════════════════════════════════════════
            ADIM 1 — SORUYU ANALİZ ET
            ════════════════════════════════════════════════════════════════════

            Cevap vermeden önce şu soruları kendine sor:
            • Kullanıcı tam olarak ne istiyor? Tek bir bilgi mi, liste mi, tablo mu, açıklama mı?
            • Soru spesifik mi ("iş ayakkabısının rengi") yoksa genel mi ("tüm ekipmanlar")?
            • Kullanıcı "hepsini ver", "tüm liste", "tam tablo" gibi ifadeler kullandı mı?

            SPESIFIK SORU → Sadece o bilgiyi ver. Tüm tabloyu veya listeyi dökme.
            GENEL SORU    → İlgili tüm bilgiyi eksiksiz ver.

            Örnekler:
            ❌ YANLIŞ: "İş ayakkabısı nedir?" sorusuna tüm ekipman tablosunu vermek
            ✓  DOĞRU:  "İş ayakkabısı nedir?" sorusuna sadece iş ayakkabısıyla ilgili satırı vermek

            ❌ YANLIŞ: "Hangi ekipmanlar var?" sorusuna 1-2 ekipman vermek
            ✓  DOĞRU:  "Hangi ekipmanlar var?" sorusuna tüm ekipman listesini vermek

            ════════════════════════════════════════════════════════════════════
            ADIM 2 — KAYNAK KURALLARI
            ════════════════════════════════════════════════════════════════════

            • YALNIZCA verilen [PARÇA X] bloklarındaki bilgiyi kullan.
            • Belgelerde yazmayan hiçbir bilgiyi ekleme, tahmin etme, tamamlama, yorum yapma.
            • Kendi genel bilginden hiçbir şey üretme — ne kadar emin olsan da.
            • Bilgi parçalarda yoksa sadece şunu söyle: "Bu bilgi yüklü belgelerde yer almıyor."
            • Birden fazla parçada bilgi varsa hepsini tara ve birleştir.

            ════════════════════════════════════════════════════════════════════
            ADIM 3 — DOĞRULUK VE EKSİKSİZLİK
            ════════════════════════════════════════════════════════════════════

            • Sayılar, kodlar, tarihler, ölçüler, seri numaraları — HİÇ DEĞİŞTİRMEDEN aktar.
            • Yuvarlama yapma, "yaklaşık" deme — belgede ne yazıyorsa onu yaz.
            • Kullanıcı açıkça istediğinde (tüm liste, tam tablo vb.) tek satır bile atlama.
            • Çelişen bilgi varsa her ikisini göster ve kaynağını belirt.

            ════════════════════════════════════════════════════════════════════
            ADIM 4 — GÖRSEL KULLANIM
            ════════════════════════════════════════════════════════════════════

            [GÖRSELLER: X adet görsel → [IMG:1], [IMG:2]] notu varsa:
            • O görseller o parçanın içeriğiyle ilgilidir.
            • Tabloda "Resim" sütunu varsa → her satıra sıradaki görseli koy: | 1 | [IMG:1] | İş Ayakkabısı |
            • Paragrafta nesne/ekipmandan bahsediliyorsa → yanına koy: "İş ayakkabısı [IMG:1]..."
            • Sadece görsel isteniyorsa → [IMG:1]
            • [GÖRSELLER: ...] notunu yanıtta gösterme — bu senin için talimat.
            • Uydurma [IMG:X] yazma. Sadece gönderilen görseller için kullan.

            ════════════════════════════════════════════════════════════════════
            ADIM 5 — YANIT FORMATI
            ════════════════════════════════════════════════════════════════════

            • Yanıtını her zaman TÜRKÇE ver.
            • [PARÇA X] etiketlerini yanıtta asla gösterme.
            • Kaynak belirtirken: (dosyaadi.pdf)
            • "Elbette", "Tabii ki", "Merhaba", "Size yardımcı olabilirim" gibi dolgu cümleleri kullanma.
            • Doğrudan yanıtla — giriş cümlesi gereksiz.
            • Tablo içeriği varsa markdown tablo formatında sun.
            • Kod içeriği varsa kod bloğu olarak sun.
            • Yanıt uzunsa bölüm başlıkları kullan.
            • Aynı bilgiyi iki kez yazma.

            ════════════════════════════════════════════════════════════════════
            ADIM 6 — TABLO İŞLEME
            ════════════════════════════════════════════════════════════════════

            [TABLO BAŞLANGIÇ]...[TABLO BİTİŞ] bloğu geldiğinde:
            • Kullanıcı tabloyla ilgili spesifik bir şey soruyorsa → sadece o satırı/bilgiyi ver.
            • Kullanıcı tüm tabloyu istiyorsa → eksiksiz markdown formatında sun.
            • Boş tablo satırlarını (| | | |) yanıta ekleme.
            • Görsel sütunu varsa ve [GÖRSELLER] notu geldiyse → görselleri sıraya göre yerleştir.

            ════════════════════════════════════════════════════════════════════
            ADIM 7 — KONUŞMA GEÇMİŞİ VE BAĞLAM
            ════════════════════════════════════════════════════════════════════

            Konuşma geçmişi sana iletilir. Her soruyu geçmiş bağlamıyla birlikte değerlendir:
            • "O", "bu", "bahsettiğin", "söylediğin", "o ekipman", "o belge" gibi zamirler
              → Geçmişten neye atıfta bulunduğunu anla, bağlamı koru.
            • "Devam et", "daha fazla ver", "diğerleri", "geri kalanı" gibi ifadeler
              → Önceki yanıtın devamını ver.
            • Her yeni soruyu konuşmanın bir parçası olarak değerlendir.
              Kullanıcı bir konuyu konuşuyorsa yeni soru da muhtemelen aynı konuyla ilgilidir.
            • Geçmiş yanıtlarında verdiğin bilgileri tekrar etme — sadece yeni bilgi ekle.

            ════════════════════════════════════════════════════════════════════
            ADIM 8 — ÇAPRAZ BELGE
            ════════════════════════════════════════════════════════════════════

            Birden fazla belgeden bilgi geliyorsa:
            • Her bilginin yanında kaynak belirt: (dosya.pdf)
            • Aynı konuda farklı belgeler varsa karşılaştırmalı sun.
            • Çelişen bilgi varsa açıkça belirt: "X dosyasında ... yazarken Y dosyasında ... yazıyor."
            """;

        var userMessage = $"""
            BELGE PARÇALARI:
            ════════════════════════════════════════════════════════════════════
            {context}
            ════════════════════════════════════════════════════════════════════

            SORU: {question}

            ÖNEMLİ TALİMATLAR:
            1. Önce soruyu ve konuşma geçmişini birlikte analiz et — tam olarak ne isteniyor?
            2. Geçmiş konuşmada bahsedilen konu varsa yeni soruyu o bağlamda değerlendir.
            3. Spesifik soru ise sadece o bilgiyi ver, tüm içeriği dökme.
            4. Genel soru ise ilgili tüm bilgiyi eksiksiz ver.
            5. Sadece belgelerdeki bilgiyi kullan, dışarıdan bilgi ekleme.
            6. [GÖRSELLER] notu olan parçalarda görselleri MUTLAKA uygun yerlere yerleştir.
            7. [PARÇA X] etiketlerini yanıtta kullanma.
            8. Türkçe yanıt ver.
            """;

        return _cfg["Llm:Provider"] switch
        {
            "Anthropic" => await CallAnthropicAsync(systemPrompt, userMessage, ct),
            "Gemini" => await CallGeminiAsync(systemPrompt, userMessage, ct),
            "Ollama" => await CallOllamaAsync(systemPrompt, userMessage, ct),
            _ => await CallOpenAiWithVisionAsync(systemPrompt, userMessage, imageUrls, ct, historyList)
        };
    }

    // ── OpenAI uyumlu + Vision ────────────────────────────────────────────
    private async Task<string> CallOpenAiWithVisionAsync(
        string system, string user, List<string> imageUrls, CancellationToken ct,
        IEnumerable<(string Role, string Content)>? history = null)
    {
        var maxTokens = int.TryParse(_cfg["Llm:MaxTokens"], out var t) ? t : 4096;

        var userContent = new List<object> { new { type = "text", text = user } };

        foreach (var imgPath in imageUrls)
        {
            try
            {
                var fullPath = Path.Combine("uploads", imgPath);
                if (!File.Exists(fullPath)) continue;
                var imgBytes = await File.ReadAllBytesAsync(fullPath, ct);
                var ext = imgPath.EndsWith(".jpg") || imgPath.EndsWith(".jpeg") ? "jpeg" : "png";
                var base64 = Convert.ToBase64String(imgBytes);
                userContent.Add(new { type = "image_url", image_url = new { url = $"data:image/{ext};base64,{base64}" } });
            }
            catch (Exception ex) { Console.WriteLine($"[LLM] Resim eklenemedi: {ex.Message}"); }
        }

        var msgList = new List<object> { new { role = "system", content = system } };
        if (history != null)
            foreach (var h in history)
                msgList.Add(new { role = h.Role, content = h.Content });

        msgList.Add(userContent.Count > 1
            ? new { role = "user", content = (object)userContent }
            : new { role = "user", content = (object)user });

        var payload = new { model = _cfg["Llm:Model"], max_tokens = maxTokens, temperature = 0.1f, messages = msgList };
        var response = await _http.PostAsJsonAsync("/openai/v1/chat/completions", payload, ct);
        await EnsureSuccessAsync(response);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        return json.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString()!.Trim();
    }

    // ── Anthropic ─────────────────────────────────────────────────────────
    private async Task<string> CallAnthropicAsync(string system, string user, CancellationToken ct)
    {
        var maxTokens = int.TryParse(_cfg["Llm:MaxTokens"], out var t) ? t : 4096;
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
        var maxTokens = int.TryParse(_cfg["Llm:MaxTokens"], out var t) ? t : 4096;
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
        return json.GetProperty("candidates")[0].GetProperty("content").GetProperty("parts")[0].GetProperty("text").GetString()!.Trim();
    }

    // ── Ollama ────────────────────────────────────────────────────────────
    private async Task<string> CallOllamaAsync(string system, string user, CancellationToken ct)
    {
        var payload = new
        {
            model = _cfg["Llm:Model"],
            stream = false,
            options = new { temperature = 0.1, num_predict = 4096 },
            messages = new[] { new { role = "system", content = system }, new { role = "user", content = user } }
        };
        var response = await _http.PostAsJsonAsync("/api/chat", payload, ct);
        await EnsureSuccessAsync(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        return json.GetProperty("message").GetProperty("content").GetString()!.Trim();
    }

    // ── Soru kalitesi kontrolü ────────────────────────────────────────────
    private async Task<bool> ValidateQuestionAsync(string question, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(question) || question.Trim().Length < 2) return false;
        try
        {
            var payload = new
            {
                model = _cfg["Llm:Model"],
                max_tokens = 10,
                temperature = 0.0f,
                messages = new[]
                {
                    new { role = "system", content = "Kullanıcının mesajını değerlendir. Mesaj anlamlı bir soru veya istek içeriyorsa 'EVET', küfür/hakaret/anlamsız/ilgisiz içerik ise 'HAYIR' döndür. Başka hiçbir şey yazma." },
                    new { role = "user",   content = question }
                }
            };
            var response = await _http.PostAsJsonAsync("/openai/v1/chat/completions", payload, ct);
            if (!response.IsSuccessStatusCode) return true;
            var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
            var answer = json.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString()?.Trim().ToUpperInvariant() ?? "";
            return answer.Contains("EVET");
        }
        catch { return true; }
    }

    // ── Hata yönetimi ─────────────────────────────────────────────────────
    private static async Task EnsureSuccessAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return;
        var body = await response.Content.ReadAsStringAsync();
        throw new HttpRequestException($"LLM API hatası [{(int)response.StatusCode}]: {body}", inner: null, statusCode: response.StatusCode);
    }
}