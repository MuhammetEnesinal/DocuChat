// DocuChat.Infrastructure/Services/LlmService.cs
using System.Net.Http.Json;
using System.Text.Json;
using DocuChat.Application.Interfaces.Services;
using DocuChat.Application.Interfaces.Repositories;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace DocuChat.Infrastructure.Services;

public class LlmService : ILlmService
{
    private readonly HttpClient _http;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _cfg;
    private readonly ILogger<LlmService> _logger;

    private readonly string _provider;
    private readonly string _model;
    private readonly int _maxTokens;
    private readonly string _apiKey;

    public LlmService(HttpClient http, IHttpClientFactory httpClientFactory, IConfiguration cfg, ILogger<LlmService> logger)
    {
        _http = http;
        _httpClientFactory = httpClientFactory;
        _cfg = cfg;
        _logger = logger;
        _provider = cfg["Llm:Provider"] ?? "OpenAI";
        _model = cfg["Llm:Model"] ?? "gpt-4o";
        _apiKey = cfg["Llm:ApiKey"] ?? string.Empty;
        _maxTokens = int.TryParse(cfg["Llm:MaxTokens"], out var t) ? t : 4096;
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
            return "Sisteme yuklenmis belgeler arasinda bu soruyla ilgili bilgi bulunamadi.";

        var contextParts = new List<string>();

        // ── Geçiş 1: her chunk'ın resim listesini topla ──────────────────
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

        // Tekil global liste — ilk geçiş sırası korunur
        var seenUrls = new HashSet<string>(StringComparer.Ordinal);
        var allUniqueImages = chunkImageLists
            .SelectMany(x => x)
            .Where(p => seenUrls.Add(p))
            .ToList();

        // Yapılandırılabilir limit (appsettings: Llm:MaxImages, varsayılan 5 — Groq hard limit)
        var maxImages = Math.Min(int.TryParse(_cfg["Llm:MaxImages"], out var mi) ? mi : 5, 5);
        var sentImageUrls = allUniqueImages.Take(maxImages).ToList();

        // Yol → 1-tabanlı global indeks eşleştirmesi
        var pathToGlobalIdx = sentImageUrls
            .Select((p, i) => (p, i))
            .ToDictionary(x => x.p, x => x.i + 1);

        // ── Geçiş 2: doğru global indekslerle context metnini oluştur ────
        for (var ci = 0; ci < chunkList.Count; ci++)
        {
            var c = chunkList[ci];
            var paths = chunkImageLists[ci];

            var cleanContent = System.Text.RegularExpressions.Regex.Replace(
                c.Content.Trim(), @"\[RESIM:[^\]]*\]", "").Trim();

            string chunkText;
            if (paths.Count > 0)
            {
                // [IMG_REF:localIdx] → [IMG:globalIdx] (gönderilmeyen resimler silinir)
                var resolvedContent = System.Text.RegularExpressions.Regex.Replace(
                    cleanContent,
                    @"\[IMG_REF:(\d+)\]",
                    m =>
                    {
                        if (int.TryParse(m.Groups[1].Value, out var localIdx)
                            && localIdx < paths.Count
                            && pathToGlobalIdx.TryGetValue(paths[localIdx], out var gIdx))
                            return $"[IMG:{gIdx}]";
                        return ""; // limit dışı resim — marker'ı kaldır
                    });

                // Inline işaret yoksa (PDF gibi) genel not ekle — sadece gönderilenleri listele
                if (!resolvedContent.Contains("[IMG:"))
                {
                    var sentFromChunk = paths
                        .Where(p => pathToGlobalIdx.ContainsKey(p))
                        .ToList();
                    if (sentFromChunk.Count > 0)
                    {
                        var nums = string.Join(", ", sentFromChunk.Select(p => $"[IMG:{pathToGlobalIdx[p]}]"));
                        resolvedContent = $"[GORSELLER: {sentFromChunk.Count} adet - {nums}]\n\n{resolvedContent}";
                    }
                }

                var headerSuffix = !string.IsNullOrWhiteSpace(c.Header) ? $" | {c.Header}" : "";
                _logger.LogDebug("[LLM Context] Parca {Index} | {FileName} - {ImageCount} gorsel", ci + 1, c.FileName, paths.Count);
                chunkText = $"[PARCA {ci + 1} | {c.FileName}{headerSuffix}]\n\n{resolvedContent}";
            }
            else
            {
                var headerSuffix = !string.IsNullOrWhiteSpace(c.Header) ? $" | {c.Header}" : "";
                chunkText = $"[PARCA {ci + 1} | {c.FileName}{headerSuffix}]\n\n{cleanContent}";
            }

            contextParts.Add(chunkText);
        }

        var context = string.Join("\n\n---\n\n", contextParts);
        var imageUrls = sentImageUrls;


        var historyList = history?.ToList() ?? new List<(string Role, string Content)>();

        var systemPrompt =
            "Sen kurumsal belge tabanlı soru-cevap asistanısın.\n" +
            "Kullanıcıların sorularını YALNIZCA sana sağlanan belge parçalarından yanıtlarsın.\n\n" +

            "━━━ TEMEL KURAL ━━━\n" +
            "Belge parçalarında olmayan hiçbir bilgiyi üretme, tahmin etme veya tamamlama.\n" +
            "Kendi genel bilginden yanıt verme. Yalnızca verilen PARÇA bloklarını kullan.\n\n" +

            "━━━ ADIM ADIM ANALİZ ━━━\n" +
            "Yanıt üretmeden önce şu adımları zihinsel olarak uygula (yanıta yazma):\n" +
            "1. Sorudaki anahtar varlıkları çıkar: isim, kod, tarih, ürün, sayı, kategori\n" +
            "2. Bu varlıkları PARÇA bloklarında sırayla ara — hangi parçalar soruyla örtüşüyor?\n" +
            "3. Parçalarda çelişen bilgi var mı? Her iki kaynağı not et\n" +
            "4. Eksik bilgi var mı? Varsa uydurma — bunu yanıtta belirt\n" +
            "5. Yanıt türünü belirle: tek değer / liste / tablo / adım / açıklama\n" +
            "Yalnızca bu analiz tamamlandıktan sonra yanıtı oluştur.\n\n" +

            "━━━ ALAKASIZ SORU ━━━\n" +
            "Aşağıdaki durumlarda hiç yanıt verme — yalnızca şu sabit cümleleri kullan:\n" +
            "• Soru belgelerle ilgisizse → \"Bu soru yüklenen belgelerle ilgili değil. Lütfen belge içerikleriyle ilgili bir soru sorun.\"\n" +
            "• Anlamsız / rastgele karakterler → \"Anlaşılır bir soru tespit edilemedi. Lütfen sorunuzu daha net ifade edin.\"\n" +
            "• Selamlama / genel bilgi isteği → \"Ben yalnızca yüklenen belgeler hakkında soru cevaplayabilirim.\"\n" +
            "Bu kuralı kesinlikle atlatma — belge parçaları verilmiş olsa bile alakasız sorulara yanıt verme.\n\n" +

            "━━━ BİLGİ YOKSA / KISMI BİLGİ ━━━\n" +
            "• Bilgi hiçbir parçada yoksa: \"Bu bilgi yüklü belgelerde yer almıyor.\"\n" +
            "• Kısmi bilgi varsa: bulunan kısmı ver, sonra \"Bu konuda belgelerde daha fazla bilgi bulunmuyor.\" ekle\n" +
            "• Çelişen bilgi varsa: her iki kaynağı da belirt — hangi belgede ne yazdığını göster\n\n" +

            "━━━ YANIT UZUNLUĞU ━━━\n" +
            "• Tek değer sorusu (tarih, kod, isim, sayı) → 1 satır cevap yeterli; paragraf açma\n" +
            "• Liste / enum sorusu → tam liste, hiçbir maddeyi atlama\n" +
            "• Prosedür / süreç sorusu → numaralı adımlar (1. 2. 3.)\n" +
            "• Karşılaştırma sorusu → markdown tablo kullan\n" +
            "• Genel açıklama sorusu → 3-5 cümle yeterli; şişirme\n\n" +

            "━━━ SORU ANALİZİ ━━━\n" +
            "• Spesifik soru (tek veri, tarih, isim, değer) → yalnızca o bilgiyi ver, tüm tabloyu dökme\n" +
            "• Genel / liste sorusu → ilgili tüm bilgiyi eksiksiz ver\n" +
            "• \"Hepsini ver\", \"tam tablo\", \"tüm liste\" ifadeleri varsa → tek satır bile atlama\n\n" +

            "━━━ ÇOKLU BELGE ━━━\n" +
            "Birden fazla belgeden parçalar gelebilir. Her parça [PARÇA N | dosyaadı] etiketiyle gelir.\n" +
            "• Soru tek belgeyle ilgiliyse → YALNIZCA o belgeden yanıtla\n" +
            "• Soru birden fazla belgeyle ilgiliyse → ilgili belgelerden al, her bilginin kaynağını belirt: (dosyaadı.pdf)\n" +
            "• Alakasız belgenin içeriğini asla yanıta katma\n\n" +

            "━━━ DOĞRULUK ━━━\n" +
            "• Sayılar, kodlar, tarihler, ölçüler değiştirmeden aktar — yuvarlama yapma\n" +
            "• Belgede ne yazıyorsa onu yaz, yorumlama veya özetleme\n" +
            "• Kullanıcı \"hepsini\" istediğinde tek satır bile atlama\n" +
            "• Tablo verisi istendiğinde başlık satırını (header) da dahil et — yalnızca veri satırlarını verme\n\n" +

            "━━━ GÖRSELLER ━━━\n" +
            "Parçada [GORSELLER: N adet - [IMG:1] [IMG:2] ...] notu varsa görselleri yerleştir:\n" +
            "• Tablo satırında nesne varsa → | Sıra | [IMG:N] | Ürün Adı | biçiminde\n" +
            "• Paragrafta nesneden bahsediliyorsa → yanına koy: Baret [IMG:1] koruyucu başlık ekipmanıdır.\n" +
            "• [GORSELLER] notunu yanıta asla yazma — bu yalnızca senin için talimattır\n" +
            "• Uydurma [IMG:N] numarası yazma — yalnızca gönderilen görseller için kullan\n\n" +

            "━━━ FORMAT KURALLARI ━━━\n" +
            "• Adım adım süreç sorusu (nasıl, hangi adımlar, prosedür) → mutlaka 1. 2. 3. numaralı liste kullan\n" +
            "• Tablo hücresi boşsa veya bilgi yoksa → \"—\" yaz, hücreyi boş bırakma\n" +
            "• Sayısal değerleri belgede nasılsa öyle aktar — yuvarlama veya birim değiştirme yapma\n" +
            "• Tek veri sorusu (tarih, isim, değer, kod) → tek satır cevap ver, paragraf açma\n" +
            "• \"Hepsini/tamamını/tüm listeyi\" isteği → tek madde bile atlamadan eksiksiz listele\n\n" +

            "━━━ YANIT FORMATI ━━━\n" +
            "• Her zaman Türkçe yanıt ver\n" +
            "• PARÇA etiketlerini yanıta yazma\n" +
            "• Dolgu cümlesi kullanma (\"Elbette\", \"Tabii ki\", \"Merhaba\" gibi) — doğrudan yanıtla\n" +
            "• Soruyu yanıtta tekrarlama\n" +
            "• Tablo içeriği → markdown tablo; kod içeriği → kod bloğu\n" +
            "• Uzun yanıtlarda bölüm başlıkları kullan — aynı bilgiyi iki kez yazma\n\n" +

            "━━━ KONUŞMA GEÇMİŞİ ━━━\n" +
            "Önceki konuşma ÖNCEKİ KONUŞMA başlığı altında iletilir.\n" +
            "• \"o\", \"bu\", \"bahsettiğin\", \"söylediğin\" gibi zamirler → geçmişten anla\n" +
            "• \"Devam et\", \"daha fazla ver\", \"diğerleri\" → önceki yanıtı sürdür\n" +
            "• Her yeni soruyu konuşmanın bir parçası olarak değerlendir\n" +
            "• Geçmiş yanıtlardaki bilgileri tekrar etme — sadece yeni bilgi ekle\n" +
            "• Kullanıcı önceki yanıtı eleştiriyorsa (\"yanlış\", \"hayır\", \"değil\") → farklı parçalara bak, alternatif bilgi sun";

        var historyPrefix = "";
        if (historyList.Any())
        {
            var histLines = historyList
                .Select(h => (h.Role.ToLower() == "user" ? "Kullanıcı" : "Asistan") + ": " + h.Content)
                .ToList();
            historyPrefix = "ÖNCEKİ KONUŞMA:\n" + string.Join("\n", histLines) + "\n\n---\n\n";
        }

        var userMessage =
            historyPrefix +
            "BELGE PARÇALARI:\n" +
            "════════════════════════════════════════════════════════════════\n" +
            context + "\n" +
            "════════════════════════════════════════════════════════════════\n\n" +
            "SORU: " + question;

        return _provider switch
        {
            "Anthropic" => await CallAnthropicAsync(systemPrompt, userMessage, ct),
            "Gemini" => await CallGeminiAsync(systemPrompt, userMessage, ct),
            "Ollama" => await CallOllamaAsync(systemPrompt, userMessage, ct),
            _ => await CallOpenAiWithVisionAsync(systemPrompt, userMessage, imageUrls, ct)
        };
    }

    public async Task<List<string>> DetectRelevantDocumentsAsync(
        string question,
        IEnumerable<(string Role, string Content)> history,
        IEnumerable<string> availableDocuments,
        CancellationToken ct = default)
    {
        var docList = availableDocuments.ToList();
        if (!docList.Any()) return new List<string>();

        var historyText = string.Join("\n", history.TakeLast(4).Select(h => h.Role + ": " + h.Content));
        var docsText = string.Join("\n", docList.Select((d, i) => (i + 1) + ". " + d));

        var docDetectSystem =
            "Sen bir belge eşleştirme asistanısın.\n" +
            "Verilen soruya cevap verebilecek belgelerin adlarını virgülle ayırarak döndür.\n" +
            "KURALLAR:\n" +
            "• Dosya adını listeden TAM OLARAK kopyala — değiştirme, kısaltma, büyük/küçük harf değiştirme\n" +
            "• Birden fazla belge ilgiliyse hepsini listele\n" +
            "• Hiçbiri ilgili değilse boş bırak\n" +
            "• Belirsizsen geniş tut — eksik bırakmak, fazla listelemekten daha kötüdür\n" +
            "Başka hiçbir şey yazma.";

        var prompt =
            "Aşağıdaki soru için hangi belgeler gerekli?\n\n" +
            "MEVCUT BELGELER:\n" + docsText + "\n\n" +
            (historyText.Length > 0 ? "SON KONUŞMA:\n" + historyText + "\n\n" : "") +
            "SORU: " + question + "\n\n" +
            "Sadece ilgili belge adlarını virgülle ayırarak yaz (örn: belge1.pdf, belge2.pdf). " +
            "İlgili belge yoksa boş bırak.";

        try
        {
            var payload = new
            {
                model = _model,
                max_tokens = 100,
                temperature = 0.0f,
                messages = new object[]
                {
                    new { role = "system", content = docDetectSystem },
                    new { role = "user",   content = prompt }
                }
            };
            var response = await _http.PostAsJsonAsync("/openai/v1/chat/completions", payload, ct);
            if (!response.IsSuccessStatusCode) return new List<string>();

            var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
            var answer = json.GetProperty("choices")[0]
                            .GetProperty("message")
                            .GetProperty("content")
                            .GetString()?.Trim() ?? "";

            _logger.LogInformation("[DocDetect] Tespit edilen belgeler: {Answer}", answer);
            if (string.IsNullOrWhiteSpace(answer)) return new List<string>();

            return answer.Split(',')
                         .Select(d => d.Trim())
                         .Where(d => !string.IsNullOrWhiteSpace(d))
                         .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[DocDetect] API çağrısı başarısız — soru: {Question}", question);
            return new List<string>();
        }
    }

    private const int GroqMaxImages = 5; // Groq hard limit

    private async Task<string> CallOpenAiWithVisionAsync(
        string system, string user, List<string> imageUrls, CancellationToken ct)
    {
        var userContent = new List<object> { new { type = "text", text = user } };

        // Groq hard limit: en fazla 5 resim — AskAsync'teki kırpma yeterli olsa da ikinci savunma katmanı
        var cappedUrls = imageUrls.Take(GroqMaxImages).ToList();
        if (imageUrls.Count > GroqMaxImages)
            _logger.LogWarning("[LLM] Resim limiti asildi: {Count} → {Max}'e kirpildi.", imageUrls.Count, GroqMaxImages);

        foreach (var imgPath in cappedUrls)
        {
            try
            {
                var fullPath = Path.Combine("uploads", imgPath);
                if (!File.Exists(fullPath))
                {
                    _logger.LogWarning("[LLM] Resim bulunamadi: {FullPath}", fullPath);
                    continue;
                }
                _logger.LogDebug("[LLM] Resim gonderiliyor: {ImgPath}", imgPath);
                var imgBytes = await File.ReadAllBytesAsync(fullPath, ct);
                var ext = imgPath.EndsWith(".jpg") || imgPath.EndsWith(".jpeg") ? "jpeg" : "png";
                var base64 = Convert.ToBase64String(imgBytes);
                userContent.Add(new { type = "image_url", image_url = new { url = $"data:image/{ext};base64,{base64}" } });
            }
            catch (Exception ex) { _logger.LogWarning("[LLM] Resim eklenemedi: {Message}", ex.Message); }
        }

        var msgList = new List<object>
        {
            new { role = "system", content = system },
            userContent.Count > 1
                ? (object)new { role = "user", content = userContent }
                : (object)new { role = "user", content = user }
        };

        var payload = new { model = _model, max_tokens = _maxTokens, temperature = 0.05f, messages = msgList };
        var response = await _http.PostAsJsonAsync("/openai/v1/chat/completions", payload, ct);
        await EnsureSuccessAsync(response);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        return json.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString()!.Trim();
    }

    private async Task<string> CallAnthropicAsync(string system, string user, CancellationToken ct)
    {
        // Anthropic header'ları DI'da set edildi — burada sadece body gönderiliyor
        using var request = new HttpRequestMessage(HttpMethod.Post, string.Empty);
        request.Content = JsonContent.Create(new
        {
            model = _model,
            max_tokens = _maxTokens,
            system,
            messages = new[] { new { role = "user", content = user } }
        });
        var response = await _http.SendAsync(request, ct);
        await EnsureSuccessAsync(response);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        return json.GetProperty("content")[0].GetProperty("text").GetString()!.Trim();
    }

    private async Task<string> CallGeminiAsync(string system, string user, CancellationToken ct)
    {
        // Gemini kendi URL'ini kullanıyor — _http yerine BaseAddress override ile gönderiyoruz
        var model = _model;
        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={_apiKey}";
        var payload = new
        {
            system_instruction = new { parts = new[] { new { text = system } } },
            contents = new[] { new { role = "user", parts = new[] { new { text = user } } } },
            generationConfig = new { maxOutputTokens = _maxTokens, temperature = 0.1 }
        };

        using var client = _httpClientFactory.CreateClient();
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

    private async Task<string> CallOllamaAsync(string system, string user, CancellationToken ct)
    {
        var payload = new
        {
            model = _model,
            stream = false,
            options = new { temperature = 0.1, num_predict = _maxTokens },
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

    public async Task<bool> IsCacheableAsync(
        string question,
        IEnumerable<(string Role, string Content)>? history = null,
        CancellationToken ct = default)
    {
        var cacheSystem =
            "Sen bir binary sınıflandırıcısın. Sana verilen sorunun önceki konuşma " +
            "geçmişine ihtiyaç duymadan tek başına tam anlamıyla anlaşılıp " +
            "anlaşılamayacağını belirle. Sadece 'evet' veya 'hayir' yaz, " +
            "başka hiçbir şey yazma.";

        var historySection = "";
        if (history != null)
        {
            var recent = history.TakeLast(3).ToList();
            if (recent.Count > 0)
            {
                var lines = recent.Select(h =>
                    (h.Role.ToLower() == "user" ? "Kullanıcı" : "Asistan") + ": " +
                    h.Content[..Math.Min(150, h.Content.Length)]);
                historySection = "SON KONUŞMA (bağlam için):\n" +
                    string.Join("\n", lines) + "\n\n";
            }
        }

        var cachePrompt =
            "Soru bağımsız ise 'evet', bağımlı ise 'hayir' yaz.\n\n" +
            "BAĞIMLI sinyaller (→ hayir):\n" +
            "• Belirsiz zamir / eksik özne: \"o\", \"bu\", \"şu\", \"onlar\", \"onu\", " +
                "\"peki ya X?\", \"kaç adet?\" (özne yok)\n" +
            "• Devam ifadesi: \"devam et\", \"kalanları\", \"diğerleri\", " +
                "\"hepsini ver\", \"geri kalanı\"\n" +
            "• Bağlamsız sıra / konum: \"2. satırı ver\", \"2. satırı getir\", " +
                "\"3. maddeyi göster\", \"bir sonraki\", \"yukarıdaki\" (hangi liste bilinmiyor)\n" +
            "• Tek başına sıra/konum: \"ilkini\", \"sonuncusunu\", \"2. olanı\"\n" +
            "• Karşılaştırma + eksik özne: \"aralarındaki fark ne?\" (hangileri belli değil)\n" +
            "• Tek kelime fiil / öznesiz eylem: \"açıkla\", \"detaylandır\", \"göster\" (ne/nereyi belli değil)\n" +
            "• Reaktif başlangıç: \"Peki\", \"Ya da\", \"Ama\", \"O zaman\" ile başlayan sorular\n" +
            "• Konuşma geçmişine doğrudan atıf: \"az önce söylediğin\", \"yukarıdaki cevap\"\n\n" +
            "BAĞIMSIZ işaretler (→ evet):\n" +
            "• Özne açıkça var: \"Baret nedir?\", \"KKE ürünleri neler?\"\n" +
            "• Tam cümle, kendine yeterli: \"Yangın tüpü nasıl kullanılır?\"\n" +
            "• Belge / konu adıyla birlikte sıra: \"KKE tablosundaki ilk 3 ürünü listele\"\n" +
            "• Tarih / kod isteği + açık özne: \"SG-102 kodu nedir?\", \"3 Mayıs 2024 tarihi ne anlama geliyor?\"\n" +
            "• \"... listele\", \"... göster\" + açık konu adı: \"tüm baret modellerini listele\"\n\n" +
            "NOT: Kısa soru ≠ bağımlı. \"Baret nedir?\" kısa ama bağımsızdır.\n\n" +
            "VARSAYILAN KURAL: Yukarıdaki BAĞIMLI sinyallerden herhangi biri varsa — diğer içerik " +
            "ne olursa olsun — 'hayir' yaz. Emin değilsen 'hayir' yaz.\n\n" +
            historySection +
            $"DEĞERLENDİR: {question}\n\n" +
            "Cevap (sadece tek kelime):";

        try
        {
            var payload = new
            {
                model = _model,
                max_tokens = 10,
                temperature = 0.0f,
                messages = new object[]
                {
                    new { role = "system", content = cacheSystem },
                    new { role = "user",   content = cachePrompt }
                }
            };

            var response = await _http.PostAsJsonAsync("/openai/v1/chat/completions", payload, ct);
            if (!response.IsSuccessStatusCode) return false; // hata durumunda cache'leme

            var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
            var answer = json.GetProperty("choices")[0]
                            .GetProperty("message")
                            .GetProperty("content")
                            .GetString()?.Trim().ToLowerInvariant() ?? "";

            // "evet" ile başlıyorsa bağımsız → cache'le; her şey başka → güvenli taraf = SKIP
            var isCacheable = answer.StartsWith("eve");
            _logger.LogDebug("[Cache] IsCacheable → '{Answer}' → {IsCacheable}", answer, isCacheable);
            return isCacheable;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("[Cache] IsCacheable hata: {Message}", ex.Message);
            return false; // hata durumunda cache'leme — yanlış WRITE'tan iyidir
        }
    }

    public async Task<string> GenerateHypotheticalDocumentAsync(string question, CancellationToken ct = default)
    {
        var system =
            "Sen bir teknik belge yazarısın.\n" +
            "Verilen soruyu yanıtlayan, gerçek bir Türkçe kurumsal belgeden alınmış gibi 2-3 cümlelik bir paragraf yaz.\n" +
            "• Türkçe teknik terminoloji ve resmi dil kullan\n" +
            "• Somut değerler, kodlar ve ürün adları içerebilir\n" +
            "• Nesnel ve kesin yaz — \"sanırım\", \"muhtemelen\", \"belki\" gibi ifadeler kullanma\n" +
            "• Sadece paragrafı döndür, başlık veya açıklama ekleme";

        var payload = new
        {
            model = _model,
            max_tokens = 200,
            temperature = 0.1f,
            messages = new object[]
            {
                new { role = "system", content = system },
                new { role = "user",   content = $"SORU: {question}" }
            }
        };

        try
        {
            var response = await _http.PostAsJsonAsync("/openai/v1/chat/completions", payload, ct);
            if (!response.IsSuccessStatusCode) return question;
            var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
            var result = json.GetProperty("choices")[0]
                            .GetProperty("message")
                            .GetProperty("content")
                            .GetString()?.Trim();
            return string.IsNullOrWhiteSpace(result) ? question : result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[HyDE] Varsayımsal belge üretilemedi — soru: {Question}", question);
            return question;
        }
    }

    public async Task<IReadOnlyList<int>> RerankChunksAsync(
        string question, IReadOnlyList<string> chunkContents, int topK, CancellationToken ct = default)
    {
        var fallback = Enumerable.Range(0, Math.Min(topK, chunkContents.Count)).ToList();

        var chunksText = string.Join("\n\n", chunkContents.Select((c, i) =>
            $"[{i + 1}] {c[..Math.Min(350, c.Length)].Trim()}"));

        var system =
            "Sen bir belge parçası sıralama asistanısın.\n" +
            "Verilen soruya en doğrudan ve eksiksiz cevap veren parçaları önce sırala.\n" +
            "Sıralama kriterleri (öncelik sırası):\n" +
            "1. Sorudaki anahtar kelimelerle doğrudan örtüşme\n" +
            "2. Somut veri içermesi: sayı, kod, tarih, liste\n" +
            "3. Bağlam sağlayan açıklayıcı bilgi\n" +
            "Sadece numaraları virgülle yaz, başka hiçbir şey yazma.";
        var user   = $"SORU: {question}\n\nPARÇALAR:\n{chunksText}\n\n" +
                     $"En ilgili {topK} parçanın numaralarını virgülle sırala (en iyi ilk). " +
                     $"Sadece rakamları yaz:";

        var payload = new
        {
            model = _model,
            max_tokens = 30,
            temperature = 0.0f,
            messages = new object[]
            {
                new { role = "system", content = system },
                new { role = "user",   content = user }
            }
        };

        try
        {
            var response = await _http.PostAsJsonAsync("/openai/v1/chat/completions", payload, ct);
            if (!response.IsSuccessStatusCode) return fallback;

            var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
            var answer = json.GetProperty("choices")[0]
                            .GetProperty("message")
                            .GetProperty("content")
                            .GetString()?.Trim() ?? "";

            _logger.LogDebug("[Rerank] LLM sıralaması: {Answer}", answer);

            var ranked = answer
                .Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => int.TryParse(s.Trim(), out var n) ? n - 1 : -1)
                .Where(i => i >= 0 && i < chunkContents.Count)
                .Distinct()
                .Take(topK)
                .ToList();

            // Eksik index'leri sıraya ekle
            var missing = Enumerable.Range(0, chunkContents.Count)
                .Where(i => !ranked.Contains(i));
            ranked.AddRange(missing.Take(topK - ranked.Count));

            return ranked.Count > 0 ? ranked : fallback;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("[Rerank] Hata: {Message}", ex.Message);
            return fallback;
        }
    }

    public async Task<string> RewriteQueryAsync(
        string question,
        IEnumerable<(string Role, string Content)> history,
        CancellationToken ct = default)
    {
        var system =
            "Sen bir arama sorgusu optimize edicisin. Kullanıcının sorusunu belge arama için netleştir:\n" +
            "• Kısaltmaları aç (KKE → Kişisel Koruyucu Ekipman) gibi\n" +
            "• Yazım hatalarını düzelt\n" +
            "• 'bu', 'o', 'bunu' gibi belirsiz zamirleri konuşma geçmişine bakarak somutlaştır\n" +
            "  DOĞRU: \"bu ürünün fiyatı\" + geçmişte \"Baret 301\" → \"Baret 301'in fiyatı nedir?\"\n" +
            "  YANLIŞ: Geçmişte özne yoksa uydurma — soruyu olduğu gibi döndür\n" +
            "• Sorunun anlamını ve dilini (Türkçe) koru — asla değiştirme\n" +
            "• Anlamlı bir değişiklik gerekmiyorsa soruyu kelimesi kelimesine döndür\n" +
            "• Soruyu kısaltma; bilgi çıkarma veya yeniden ifade etme\n" +
            "YALNIZCA yeniden yazılmış soruyu döndür. Açıklama, tırnak işareti veya başka metin ekleme.";

        var historyLines = history.TakeLast(4)
            .Select(h => (h.Role == "user" ? "Kullanıcı" : "Asistan") + ": " +
                         h.Content[..Math.Min(200, h.Content.Length)])
            .ToList();

        var user = historyLines.Count > 0
            ? $"SON KONUŞMA:\n{string.Join("\n", historyLines)}\n\nSORU: {question}"
            : $"SORU: {question}";

        var payload = new
        {
            model = _model,
            max_tokens = 150,
            temperature = 0.1f,
            messages = new object[]
            {
                new { role = "system", content = system },
                new { role = "user",   content = user }
            }
        };

        try
        {
            var response = await _http.PostAsJsonAsync("/openai/v1/chat/completions", payload, ct);
            if (!response.IsSuccessStatusCode) return question;
            var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
            var rewritten = json.GetProperty("choices")[0].GetProperty("message")
                                .GetProperty("content").GetString()?.Trim() ?? question;
            return rewritten.Length > 0 && rewritten.Length < question.Length * 4 ? rewritten : question;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[RewriteQuery] Sorgu yeniden yazılamadı — soru: {Question}", question);
            return question;
        }
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return;
        var body = await response.Content.ReadAsStringAsync();
        throw new HttpRequestException(
            $"LLM API hatasi [{(int)response.StatusCode}]: {body}",
            inner: null, statusCode: response.StatusCode);
    }
}