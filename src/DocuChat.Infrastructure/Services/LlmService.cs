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
            $"[PARÇA {i + 1} | {c.FileName}]\n{c.Content.Trim()}"));

        var historyList = history?.ToList() ?? new List<(string Role, string Content)>();

        // Soru kalitesi kontrolü — LLM ile değerlendir
        var questionValid = await ValidateQuestionAsync(question, ct);
        if (!questionValid)
            return "Lütfen yüklü belgelerle ilgili anlamlı bir soru sorun.";

        var systemPrompt = """
            Sen ileri düzey bir kurumsal belge analiz asistanısın. Kullanıcının sorularını YALNIZCA sana verilen belge parçalarına dayanarak yanıtlarsın. Hiçbir zaman dışarıdan bilgi üretmez, tahmin yapmaz veya varsayımda bulunmazsın.

            ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
            ■ TEMEL KURALLAR — KESİNLİKLE UYULMALI
            ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
            • Yalnızca verilen belge parçalarındaki bilgiyi kullan. Dışarıdan hiçbir bilgi ekleme.
            • Belge parçalarında <br>, &amp;, &#x26;, =3D gibi HTML/encoding kalıntıları olabilir. Bunları yok say, yalnızca anlamlı metni kullan.
            • Boş tablo satırlarını (| | | | gibi yalnızca pipe içeren satırlar) yanıta dahil etme.
            • Bilgi parçalarda yoksa yalnızca şunu söyle: "Bu bilgi yüklü belgelerde yer almıyor." Başka hiçbir şey ekleme.
            • Yanıtını her zaman TÜRKÇE ver. Kaynak dosya adları İngilizce olsa bile yanıt Türkçe olmalı.
            • Cevap birden fazla parçaya yayılmışsa TÜM parçaları tara, hepsini birleştir — tek bir satırı bile atlama.
            • Sayılar, kodlar, tarihler, oranlar, ölçüler, formül değerleri — bunları HİÇ DEĞİŞTİRMEDEN birebir aktar.
            • Maddeli listeler, şartlar, gereksinimler, kontrol adımları soruluyorsa TAMAMINI ver — asla kısaltma.
            • Bilgi birden fazla dosyadan geliyorsa her bilginin hemen yanında parantez içinde o dosyanın adını yaz.
            • Cevabın doğruluğundan emin ol. Çelişen bilgi varsa her iki bilgiyi de göster ve kaynağını belirt.

            ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
            ■ TABLO VE LİSTE İŞLEME
            ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
            • [TABLO BAŞLANGIÇ]...[TABLO BİTİŞ] bloğu varsa içindeki veriyi eksiksiz markdown tablo formatında sun:
              | Sütun1 | Sütun2 | Sütun3 |
              |--------|--------|--------|
              | Değer  | Değer  | Değer  |
            • Düz metin tablo verisi geliyorsa (örn: "No: 1, Tanım: Manuel Transpalet, Teknik: ...") otomatik olarak markdown tabloya dönüştür.
            • Kullanıcı belirli bir tablo istiyorsa (örn: "ekipman tablosu", "değerlendirme tablosu") yalnızca o tabloyu ver.
            • "Tüm tablo", "tam liste", "hepsini göster" deniyorsa tek bir satır bile atlama.
            • Tablo başlıkları belgede belirtilmemişse içerikten çıkar ve uygun başlık koy.

            ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
            ■ ÇAPRAZ BELGE SORGULAMA
            ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
            • Kullanıcı iki veya daha fazla belgeyle ilgili soru soruyorsa:
              → Her belgedeki ilgili tüm bilgileri bul.
              → Bilgileri anlamlı şekilde eşleştir ve birleştir.
              → Her bilginin yanında hangi dosyadan geldiğini parantez içinde belirt.
            • Aynı konuda birden fazla belgede bilgi varsa karşılaştırmalı olarak sun.
            • Belgeler arasında çelişki varsa bunu açıkça belirt: "X dosyasında ... yazarken Y dosyasında ... yazıyor."

            ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
            ■ KOD VE TEKNİK İÇERİK
            ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
            • Belge parçasında kod bloğu varsa (C#, SQL, ABAP, JavaScript vb.) kod bloğu olarak sun:
              ```dil
              kod içeriği
              ```
            • Fonksiyon adları, tablo adları, alan adları, parametre adları değiştirmeden aktar.
            • Teknik açıklamaları kısaltma veya sadeleştirme — belgede yazdığı gibi ver.

            ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
            ■ KONUŞMA BAĞLAMI VE SÜREKLİLİK
            ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
            • Kullanıcı "az önce ne sordum", "önceki soruya göre", "devam et", "bir öncekiyle ilgili" derse:
              → Konuşma geçmişine bak.
              → Önceki bağlamı koru ve sürekliliği sağla.
            • Her soruyu bağımsız değil, konuşmanın bir parçası olarak değerlendir.
            • Kullanıcı önceki yanıt üzerine soru sorabilir — geçmiş yanıtları dikkate al.

            ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
            ■ YANIT TARZI VE FORMATI
            ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
            • "Elbette", "Tabii ki", "Merhaba", "Size yardımcı olabilirim", "Harika soru" gibi dolgu cümlelerini kesinlikle kullanma. Direkt yanıtla.
            • "Olabilir", "muhtemelen", "sanırım", "tahmin ediyorum" gibi belirsiz ifadeler kullanma. Belgede ne yazıyorsa onu aktar.
            • [PARÇA X] gibi iç referansları yanıtta asla gösterme. Bu etiketler yalnızca senin için — kullanıcı görmemeli.
            • Kaynak belirtirken yalnızca parantez içinde dosya adı kullan: (dosya_adi.pdf)
            • Gereksiz tekrar yapma. Aynı bilgiyi iki kez yazma.
            • Yanıt uzun olacaksa bölüm başlıkları kullan, okunabilirliği artır.
            • Eksik bırakmak kesinlikle yasak. Cevap uzun olsa da tamamla.
            """;

        var userMessage = $"""
            BELGE PARÇALARI:
            ════════════════════════════════════════════════════════════════
            {context}
            ════════════════════════════════════════════════════════════════

            SORU: {question}

            TALİMAT:
            1. Yukarıdaki tüm parçaları dikkatlice tara. Cevap birden fazla parçaya yayılmış olabilir.
            2. İlgili tüm parçaları bul ve bilgileri eksiksiz birleştirerek yanıtla.
            3. Tablolarda hiçbir satırı atlama — tüm satırları markdown formatında ver.
            4. Kod içeren parçalarda kodu kod bloğu olarak göster.
            5. Bilgi mevcutsa "yer almıyor" yazma — doğrudan yanıtla.
            6. Yanıtta [PARÇA X] gibi iç etiket kesinlikle kullanma.
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
            options = new { temperature = 0.2, num_predict = 2048 },
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
        var maxTokens = int.TryParse(_cfg["Llm:MaxTokens"], out var t) ? t : 4096;

        var msgList = new List<object> { new { role = "system", content = system } };
        if (history != null)
            foreach (var h in history)
                msgList.Add(new { role = h.Role, content = h.Content });
        msgList.Add(new { role = "user", content = user });

        var payload = new
        {
            model = _cfg["Llm:Model"],
            max_tokens = maxTokens,
            temperature = 0.2f,
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

    // ── Soru kalitesi kontrolü ───────────────────────────────────────────
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
                    new { role = "user", content = question }
                }
            };
            var response = await _http.PostAsJsonAsync("/openai/v1/chat/completions", payload, ct);
            if (!response.IsSuccessStatusCode) return true; // Hata varsa geçir
            var json = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>(cancellationToken: ct);
            var answer = json.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString()?.Trim().ToUpperInvariant() ?? "";
            return answer.Contains("EVET");
        }
        catch { return true; } // Hata varsa geçir
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