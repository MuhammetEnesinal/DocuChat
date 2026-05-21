// DocuChat.Infrastructure/Services/LlmService.cs
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using DocuChat.Application.Common;
using DocuChat.Application.Interfaces.Services;
using DocuChat.Application.Interfaces.Repositories;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace DocuChat.Infrastructure.Services.Ai;

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
    // Resim sayısı limiti (appsettings: Llm:MaxImages, varsayılan 16).
    // Tek kontrol noktası — kod içinde sabit yok, sadece config.
    private readonly int _maxImages;
    // Chat completions path. Default OpenAI-compat (Groq, OpenAI). Gemini-OpenAI-compat için
    // appsettings'ten override edilebilir: "Llm:ChatCompletionsPath": "/v1beta/openai/chat/completions"
    private readonly string _chatCompletionsPath;

    // ── Helper LLM (yardımcı çağrılar için ayrı/ucuz model) ─────────────────
    // AskAsync ana cevap için Llm config'i kullanır; tüm diğer çağrılar
    // (Rewrite, IsCacheable, DetectDocs, Clarify, HyDE, Rerank, ValidateCached,
    // ValidateQuality, Summary) LlmHelper config'ini kullanır.
    // LlmHelper yoksa Llm config'ine fallback eder (geriye dönük uyumluluk).
    private readonly string _helperBaseUrl;
    private readonly string _helperModel;
    private readonly string _helperApiKey;
    private readonly string _helperPath;

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
        _maxImages = int.TryParse(cfg["Llm:MaxImages"], out var mi) ? mi : 16;
        _chatCompletionsPath = cfg["Llm:ChatCompletionsPath"] ?? "/openai/v1/chat/completions";

        // Helper config — yoksa Llm config'ine fallback
        _helperBaseUrl = cfg["LlmHelper:BaseUrl"] ?? cfg["Llm:BaseUrl"] ?? "https://api.groq.com";
        _helperModel = cfg["LlmHelper:Model"] ?? _model;
        _helperApiKey = cfg["LlmHelper:ApiKey"] ?? _apiKey;
        _helperPath = cfg["LlmHelper:ChatCompletionsPath"] ?? "/openai/v1/chat/completions";
    }

    /// <summary>
    /// Helper LLM çağrıları için yeni HttpClient oluşturur.
    /// IHttpClientFactory handler'ı havuzlar; client.Dispose() handler'ı bırakmaz.
    /// </summary>
    private HttpClient CreateHelperClient()
    {
        var client = _httpClientFactory.CreateClient();
        client.BaseAddress = new Uri(_helperBaseUrl);
        client.Timeout = TimeSpan.FromMinutes(2);
        if (!string.IsNullOrEmpty(_helperApiKey))
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _helperApiKey);
        return client;
    }

    /// <summary>
    /// Helper LLM endpoint'ine POST. Tüm 9 yardımcı method (Rewrite, IsCacheable,
    /// DetectDocs, Clarify, HyDE, Rerank, ValidateCached, ValidateQuality, Summary) bunu kullanır.
    /// Ana cevap (AskAsync) ise _http üzerinden Llm config'i kullanır.
    /// </summary>
    private async Task<HttpResponseMessage> PostHelperAsync(object payload, CancellationToken ct)
    {
        var client = CreateHelperClient();
        return await client.PostAsJsonAsync(_helperPath, payload, ct);
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

        // Resim limiti tek kaynak: appsettings.json → "Llm:MaxImages" (varsayılan 16).
        // Gemini 2.5 Flash teknik olarak 3600'e kadar destekler; istediğin sayıyı config'e yaz.
        var sentImageUrls = allUniqueImages.Take(_maxImages).ToList();

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
            "Kendi genel bilginden yanıt verme. Yalnızca verilen PARCA bloklarını kullan.\n" +
            "PARCA, chunk, belge parçası gibi sistem etiketlerini ASLA yanıtta kullanma — bunlar yalnızca arama içindir.\n" +
            "Soruyu yanıtta asla tekrarlama — \"Sorunuz:\", \"Şunu sordunuz:\" gibi echo kalıpları kullanma — doğrudan yanıtla.\n\n" +

            "━━━ ADIM ADIM ANALİZ ━━━\n" +
            "Yanıt üretmeden önce şu adımları zihinsel olarak uygula (yanıta yazma):\n" +
            "1. Sorudaki anahtar varlıkları çıkar: isim, kod, tarih, ürün, sayı, kategori\n" +
            "2. Bu varlıkları PARÇA bloklarında sırayla ara — hangi parçalar soruyla örtüşüyor?\n" +
            "3. Parçalarda çelişen bilgi var mı? Her iki kaynağı not et\n" +
            "4. Eksik bilgi var mı? Varsa uydurma — bunu yanıtta belirt\n" +
            "5. Yanıt türünü belirle: tek değer / liste / tablo / adım / açıklama\n" +
            "Yalnızca bu analiz tamamlandıktan sonra yanıtı oluştur.\n\n" +

            "━━━ ALAKASIZ SORU ━━━\n" +
            "ÖNEMLİ: Sana BELGE PARÇALARI bölümünde içerik gönderildiyse, soru o içerikle ilgilidir — \"ilgisiz\" deme ve yanıtla.\n" +
            "Yalnızca PARÇA bloklarında hiç alakalı içerik YOKSA aşağıdaki sabit cümleleri kullan:\n" +
            "• Soru belgelerle ilgisizse → \"Bu soru yüklenen belgelerle ilgili değil. Lütfen belge içerikleriyle ilgili bir soru sorun.\"\n" +
            "• Anlamsız / rastgele karakterler → \"Anlaşılır bir soru tespit edilemedi. Lütfen sorunuzu daha net ifade edin.\"\n" +
            "• Selamlama / genel bilgi isteği → \"Ben yalnızca yüklenen belgeler hakkında soru cevaplayabilirim.\"\n\n" +

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
            "• Tablo verisi istendiğinde başlık satırını (header) da dahil et — yalnızca veri satırlarını verme\n" +
            "• Liste veya tablo istendiğinde parçada kaç satır/madde varsa hepsini yaz — \"...\" veya \"vb.\" kullanma\n" +
            "• Uzun liste olsa bile kısaltma — kullanıcı \"hepsini\" demese de tam listeyi ver\n" +
            "• Parçada bir bilgi varsa yanıtta kullan — atlama kararı verme\n\n" +

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
            "• \"Hepsini/tamamını/tüm listeyi\" isteği → tek madde bile atlamadan eksiksiz listele\n" +
            "• Markdown tablo yazarken MUTLAKA başlık satırı + ayraç satırı (|---|---|) ekle — başlıksız tablo yazma\n" +
            "• Tablo sütun sayısı tüm satırlarda aynı olsun — eksik hücre bırakma, her satırı eksiksiz doldur\n" +
            "• Liste yazarken her maddeyi ayrı satıra yaz — virgülle yan yana sıralama yapma\n" +
            "• Uzun tablolarda satır atlamadan tüm veriyi yaz — tablo ortasında \"...\" kullanma\n\n" +

            "━━━ YANIT FORMATI ━━━\n" +
            "• Her zaman Türkçe yanıt ver\n" +
            "• PARCA etiketlerini ve \"parça\", \"chunk\", \"belge parçası\" ifadelerini yanıta yazma\n" +
            "  YANLIŞ: \"Bu parçada belirtildiğine göre...\" → DOĞRU: \"Belgede...\" veya \"[dosyaadı]'na göre...\"\n" +
            "• Dolgu cümlesi kullanma (\"Elbette\", \"Tabii ki\", \"Merhaba\" gibi) — doğrudan yanıtla\n" +
            "• Soruyu yanıtta tekrarlama — başlık olarak da echo etme; DOĞRUDAN yanıtla\n" +
            "• Tablo içeriği → markdown tablo; kod içeriği → kod bloğu\n" +
            "• Uzun yanıtlarda bölüm başlıkları kullan — aynı bilgiyi iki kez yazma\n\n" +

            "━━━ KONUŞMA GEÇMİŞİ ━━━\n" +
            "Önceki konuşma gerçek mesaj geçmişi olarak iletilir (sıradaki mesajlar).\n" +
            "• \"o\", \"bu\", \"bahsettiğin\", \"söylediğin\" gibi zamirler → geçmişten anla\n" +
            "• \"Devam et\", \"daha fazla ver\", \"diğerleri\" → önceki yanıtı sürdür\n" +
            "• Her yeni soruyu konuşmanın bir parçası olarak değerlendir\n" +
            "• Geçmiş yanıtlardaki bilgileri tekrar etme — sadece yeni bilgi ekle\n" +
            "• Kullanıcı önceki yanıtı eleştiriyorsa (\"yanlış\", \"hayır\", \"değil\") → farklı parçalara bak, alternatif bilgi sun";

        var historyMessages = historyList.Select(h =>
        {
            var isUser = h.Role.Equals("user", StringComparison.OrdinalIgnoreCase);
            var maxLen = isUser ? 2000 : 3000;
            var content = h.Content.Length > maxLen ? h.Content[..maxLen] + "…" : h.Content;
            return (Role: h.Role.ToLowerInvariant(), Content: content);
        }).ToList();

        var userMessage =
            "BELGE PARÇALARI:\n" +
            "════════════════════════════════════════════════════════════════\n" +
            context + "\n" +
            "════════════════════════════════════════════════════════════════\n\n" +
            "SORU: " + question;

        return _provider switch
        {
            "Anthropic" => await CallAnthropicAsync(systemPrompt, userMessage, historyMessages, ct),
            "Gemini" => await CallGeminiAsync(systemPrompt, userMessage, historyMessages, ct),
            "Ollama" => await CallOllamaAsync(systemPrompt, userMessage, ct),
            _ => await CallOpenAiWithVisionAsync(systemPrompt, userMessage, imageUrls, historyMessages, ct)
        };
    }

    public async Task<List<string>> DetectRelevantDocumentsAsync(
        string question,
        IEnumerable<(string Role, string Content)> history,
        IEnumerable<(string FileName, string? Summary)> availableDocuments,
        CancellationToken ct = default)
    {
        var docList = availableDocuments.ToList();
        if (!docList.Any()) return new List<string>();

        var historyText = string.Join("\n", history.TakeLast(4).Select(h => h.Role + ": " + h.Content));
        // Belge özetli formatta: "1. dosya.pdf — kısa konu özeti"
        var docsText = string.Join("\n", docList.Select((d, i) =>
        {
            var line = (i + 1) + ". " + d.FileName;
            return string.IsNullOrWhiteSpace(d.Summary) ? line : $"{line} — {d.Summary}";
        }));

        var docDetectSystem =
            "Sen bir belge eşleştirme asistanısın.\n" +
            "Verilen soruya cevap verebilecek belgelerin adlarını virgülle ayırarak döndür.\n" +
            "KURALLAR:\n" +
            "• Dosya adını listeden TAM OLARAK kopyala — değiştirme, kısaltma, büyük/küçük harf değiştirme\n" +
            "• Birden fazla belge ilgiliyse hepsini listele\n" +
            "• Hiçbiri ilgili değilse boş bırak\n" +
            "• Hangi belgenin alakalı olduğundan EMİN DEĞİLSEN sadece \"ALL\" yaz (büyük harf, virgülsüz)\n" +
            "• \"ALL\" yazarsan sistem tüm belgelerde arar — bu güvenli yedektir\n" +
            "• ASLA rastgele tahmin yapma; şüphedeysen ALL yaz\n" +
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
                model = _helperModel,
                max_tokens = 100,
                temperature = 0.0f,
                messages = new object[]
                {
                    new { role = "system", content = docDetectSystem },
                    new { role = "user",   content = prompt }
                }
            };
            var response = await PostHelperAsync(payload, ct);
            if (!response.IsSuccessStatusCode) return new List<string>();

            var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
            var answer = json.GetProperty("choices")[0]
                            .GetProperty("message")
                            .GetProperty("content")
                            .GetString()?.Trim() ?? "";

            _logger.LogInformation("[DocDetect] Tespit edilen belgeler: {Answer}", answer);
            if (string.IsNullOrWhiteSpace(answer)) return new List<string>();

            // "ALL" sentinel: LLM emin değil — ChatUseCase bunu fallback için yorumlayacak
            if (answer.Trim().Equals("ALL", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation("[DocDetect] LLM 'ALL' döndü — tüm belgelerde aranacak");
                return new List<string> { "__ALL__" };
            }

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

    private async Task<string> CallOpenAiWithVisionAsync(
        string system, string user, List<string> imageUrls,
        IReadOnlyList<(string Role, string Content)> historyMessages,
        CancellationToken ct)
    {
        var userContent = new List<object> { new { type = "text", text = user } };

        // Groq hard limit: en fazla 5 resim — AskAsync'teki kırpma yeterli olsa da ikinci savunma katmanı
        // İkinci savunma katmanı — AskAsync zaten _maxImages ile kırpıyor.
        var cappedUrls = imageUrls.Take(_maxImages).ToList();
        if (imageUrls.Count > _maxImages)
            _logger.LogWarning("[LLM] Resim limiti asildi: {Count} → {Max}'e kirpildi.", imageUrls.Count, _maxImages);

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

        var msgList = new List<object> { new { role = "system", content = system } };
        foreach (var h in historyMessages)
            msgList.Add(new { role = h.Role, content = h.Content });
        msgList.Add(userContent.Count > 1
            ? (object)new { role = "user", content = userContent }
            : (object)new { role = "user", content = user });

        // Ana cevap (Gemini Flash) — _http ve _model kullanır, helper değil
        var payload = new { model = _model, max_tokens = _maxTokens, temperature = 0.05f, messages = msgList };
        var response = await _http.PostAsJsonAsync(_chatCompletionsPath, payload, ct);
        await EnsureSuccessAsync(response);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        return json.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString()!.Trim();
    }

    private async Task<string> CallAnthropicAsync(
        string system, string user,
        IReadOnlyList<(string Role, string Content)> historyMessages,
        CancellationToken ct)
    {
        var msgList = new List<object>();
        foreach (var h in historyMessages)
            msgList.Add(new { role = h.Role, content = h.Content });
        msgList.Add(new { role = "user", content = user });

        // Anthropic header'ları DI'da set edildi — burada sadece body gönderiliyor
        // Ana cevap → _model (helper değil)
        using var request = new HttpRequestMessage(HttpMethod.Post, string.Empty);
        request.Content = JsonContent.Create(new
        {
            model = _model,
            max_tokens = _maxTokens,
            system,
            messages = msgList
        });
        var response = await _http.SendAsync(request, ct);
        await EnsureSuccessAsync(response);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        return json.GetProperty("content")[0].GetProperty("text").GetString()!.Trim();
    }

    private async Task<string> CallGeminiAsync(
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

        // Gemini kendi URL'ini kullanıyor — _http yerine BaseAddress override ile gönderiyoruz
        var model = _model;
        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={_apiKey}";
        var payload = new
        {
            system_instruction = new { parts = new[] { new { text = system } } },
            contents,
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
        // Ana cevap (Ollama) → _model (helper değil)
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
            var recent = history.TakeLast(6).ToList();
            if (recent.Count > 0)
            {
                var lines = recent.Select(h =>
                    (h.Role.Equals("user", StringComparison.OrdinalIgnoreCase) ? "Kullanıcı" : "Asistan") + ": " +
                    h.Content[..Math.Min(300, h.Content.Length)]);
                historySection = "SON KONUŞMA (bağlam için):\n" +
                    string.Join("\n", lines) + "\n\n";
            }
        }

        var cachePrompt =
            "Soru bağımsız ise 'evet', bağımlı ise 'hayir' yaz.\n\n" +
            "BAĞIMLI sinyaller (→ hayir):\n" +
            "• Belirsiz zamir / eksik özne: \"o\", \"bu\", \"şu\", \"onlar\", \"onu\", " +
                "\"peki ya X?\", \"kaç adet?\" (neyin kaçı belli değil)\n" +
            "• Devam ifadesi: \"devam et\", \"kalanları\", \"diğerleri\", " +
                "\"hepsini ver\", \"geri kalanı\"\n" +
            "• Bağlamsız sıra / konum: \"2. satırı ver\", \"bir sonraki\", \"yukarıdaki\" (hangi liste bilinmiyor)\n" +
            "• Tek başına sıra/konum — özne yok: \"ilkini\", \"sonuncusunu\", \"2. olanı\"\n" +
            "• Karşılaştırma + eksik özne: \"aralarındaki fark ne?\" (hangileri belli değil)\n" +
            "• Öznesiz tek eylem: \"açıkla\", \"detaylandır\", \"göster\" (ne/nereyi belli değil)\n" +
            "• Reaktif başlangıç: \"Peki\", \"Ya da\", \"Ama\", \"O zaman\" ile başlayan sorular\n" +
            "• Geçmişe doğrudan atıf: \"az önce söylediğin\", \"yukarıdaki cevap\", \"geçen sefer\", \"bir önceki\", \"bahsettiğin\"\n" +
            "• Bağlamlı konum referansı: \"yukarıdaki\", \"aşağıdaki\", \"ilk madde\", \"son madde\" (hangi liste belli değil)\n\n" +
            "BAĞIMSIZ işaretler (→ evet):\n" +
            "• Özne açıkça var: \"Baret nedir?\", \"KKE ürünleri neler?\", \"SG-102 nedir?\"\n" +
            "• Kısaltma tek başına bile bağımsızdır: \"KKE?\", \"İSG?\", \"PPE?\" → özne açık\n" +
            "• Ürün/kod/tarih adıyla birlikte: \"SG-102 kodu nedir?\", \"Baret 301'in fiyatı?\"\n" +
            "• Tam cümle, kendine yeterli: \"Yangın tüpü nasıl kullanılır?\"\n" +
            "• Belge / konu adıyla birlikte sıra: \"KKE tablosundaki ilk 3 ürünü listele\"\n" +
            "• \"... listele\", \"... göster\" + açık konu adı: \"tüm baret modellerini listele\"\n" +
            "• Teknik terim (ürün kodu, model numarası, kısaltma) içeriyorsa VE zamir yoksa → evet\n" +
            "• Konuşma geçmişi yoksa ve soru tam bir özne + fiil içeriyorsa → evet\n\n" +
            "KRİTİK KURAL — KISA SORU ≠ BAĞIMLI:\n" +
            "• \"Baret nedir?\" → kısa ama özne var → evet\n" +
            "• \"KKE?\" → kısa ama kısaltma özne → evet\n" +
            "• \"Fiyatı?\" → kısa VE özne yok → hayır\n" +
            "• \"O ne?\" → zamir → hayır\n" +
            "Uzunluk değil, özne varlığı belirler.\n\n" +
            "VARSAYILAN KURAL: BAĞIMLI sinyal AÇIKÇA yoksa 'evet' yaz. " +
            "Sadece yukarıdaki BAĞIMLI listesinden en az bir sinyal varsa 'hayir' yaz. " +
            "Geçmişe herhangi bir referans varsa veya özne belirsizse → 'hayir'. " +
            "Şüphe durumunda 'hayir' — yanlış cache okumak, cache kaçırmaktan daha kötüdür.\n\n" +
            historySection +
            $"DEĞERLENDİR: {question}\n\n" +
            "Cevap (sadece tek kelime):";

        try
        {
            var payload = new
            {
                model = _helperModel,
                max_tokens = 10,
                temperature = 0.0f,
                messages = new object[]
                {
                    new { role = "system", content = cacheSystem },
                    new { role = "user",   content = cachePrompt }
                }
            };

            var response = await PostHelperAsync(payload, ct);
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
            model = _helperModel,
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
            var response = await PostHelperAsync(payload, ct);
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
            model = _helperModel,
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
            var response = await PostHelperAsync(payload, ct);
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

    public async Task<List<string>> GenerateClarificationsAsync(
        string question,
        IEnumerable<(string Role, string Content)> history,
        IEnumerable<string>? availableDocuments = null,
        CancellationToken ct = default)
    {
        var docSection = "";
        var docList = availableDocuments?.ToList();
        if (docList?.Count > 0)
        {
            var docLines = string.Join("\n", docList.Select((d, i) => $"{i + 1}. {d}"));
            docSection =
                "\nMEVCUT BELGELER (seçenekler YALNIZCA bu belgelerle alakalı olmalı):\n" +
                docLines +
                "\nBu belgelerde olmayan konular hakkında kesinlikle seçenek üretme.\n";
        }

        var system =
            "Sen bir soru netleştirme asistanısın.\n" +
            "Kullanıcının girdisi aşağıdaki durumlarda netleştirme seçeneği üret:\n" +
            "  A) Yazım hatası veya eksik/yarım kelime: 'anuel Transp' → 'Manuel Transpalet hakkında bilgi verir misiniz?'\n" +
            "  B) Belirsiz zamir veya eksik özne: 'Fiyatı?' + geçmişte konu varsa → ilgili seçenekler\n" +
            "  C) Konuşma geçmişine bağlı ama özne belirsiz\n" +
            "KURALLAR:\n" +
            "• Her seçenek tam, bağımsız, doğru Türkçe soru cümlesi olmalı\n" +
            "• Yazım hatasını düzelt — uydurma; geçmişte veya belgede geçen gerçek isimleri kullan\n" +
            "• Özne eklerken YALNIZCA kullanıcının yazdığı kelimeleri, geçmişteki kullanıcı mesajlarını veya MEVCUT BELGELER listesini kullan\n" +
            "• Belgede olmayan konular, özellikler veya veriler hakkında seçenek üretme\n" +
            "• Soru açık, eksiksiz ve tek anlamlıysa → BOŞ döndür\n" +
            "• Seçenekleri YALNIZCA | karakteriyle ayır, başka hiçbir şey yazma\n" +
            "Örnek — yazım hatası: 'anuel Transp' → 'Manuel Transpalet hakkında bilgi verir misiniz?'\n" +
            "Örnek — belirsiz: 'Fiyatı?' + belgede 'Baret' var → 'Baret'in fiyatı nedir?'\n" +
            "Örnek — açık: 'Baret 301 nedir?' → (boş döndür)" +
            docSection;

        var histLines = history.TakeLast(4)
            .Select(h => (h.Role.Equals("user", StringComparison.OrdinalIgnoreCase) ? "Kullanıcı" : "Asistan") +
                         ": " + h.Content[..Math.Min(200, h.Content.Length)])
            .ToList();

        var user = histLines.Count > 0
            ? $"SON KONUŞMA:\n{string.Join("\n", histLines)}\n\nBELİRSİZ SORU: {question}\n\nSeçenekler (boş veya | ile ayrılmış):"
            : $"SORU: {question}\n\nSeçenekler (boş veya | ile ayrılmış):";

        try
        {
            var payload = new
            {
                model = _helperModel,
                max_tokens = 200,
                temperature = 0.2f,
                messages = new object[]
                {
                    new { role = "system", content = system },
                    new { role = "user",   content = user }
                }
            };

            var response = await PostHelperAsync(payload, ct);
            if (!response.IsSuccessStatusCode) return new List<string>();

            var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
            var answer = json.GetProperty("choices")[0]
                            .GetProperty("message")
                            .GetProperty("content")
                            .GetString()?.Trim() ?? "";

            if (string.IsNullOrWhiteSpace(answer)) return new List<string>();

            var options = answer
                .Split('|', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => s.Length > 5 && s.Length < 250)
                .Take(5)
                .ToList();

            _logger.LogDebug("[Clarify] '{Question}' → {Count} seçenek", question, options.Count);
            return options.Count >= 2 ? options : new List<string>();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Clarify] Seçenek üretilemedi — soru: {Question}", question);
            return new List<string>();
        }
    }

    public async Task<string> RewriteQueryAsync(
        string question,
        IEnumerable<(string Role, string Content)> history,
        CancellationToken ct = default)
    {
        var system =
            "Sen bir arama sorgusu optimize edicisin. Kullanıcının sorusunu belge arama için netleştir:\n" +
            "• Kısaltmaları aç: KKE → Kişisel Koruyucu Ekipman gibi — ASLA tam kelimeyi kısaltmaya çevirme\n" +
            "  YANLIŞ: \"Kişisel Koruyucu Ekipman\" → \"KKE\" — bunu yapma\n" +
            "• Yazım hatalarını düzelt\n" +
            "• 'bu', 'o', 'bunu' gibi belirsiz zamirleri YALNIZCA geçmişte AÇIKÇA geçen kelimelerle somutlaştır\n" +
            "  DOĞRU: \"bu ürünün fiyatı\" + geçmişte kullanıcı \"Baret 301\" yazdıysa → \"Baret 301'in fiyatı nedir?\"\n" +
            "  YANLIŞ: Geçmişte özne yoksa veya asistan cevabından çıkarım yapıyorsan — soruyu olduğu gibi döndür\n" +
            "  YANLIŞ: \"Fiyatı?\" + geçmişte yalnızca \"Baret nedir?\" → \"Baret 301'in fiyatı\" YAZMA — özne belli değil\n" +
            "• Geçmişte geçmeyen hiçbir model adı, kod, numara veya özellik ekleme — asistan cevabından çıkarım yapma\n" +
            "• Sorunun anlamını ve dilini (Türkçe) koru — asla değiştirme\n" +
            "• Anlamlı bir değişiklik gerekmiyorsa soruyu kelimesi kelimesine döndür\n" +
            "• Soruyu kısaltma, özetleme veya farklı bir anlama çekme\n" +
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
            model = _helperModel,
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
            var response = await PostHelperAsync(payload, ct);
            if (!response.IsSuccessStatusCode) return question;
            var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
            var rewritten = json.GetProperty("choices")[0].GetProperty("message")
                                .GetProperty("content").GetString()?.Trim() ?? question;

            var knownPrefixes = new[] { "Kullanıcının sorusunu", "Sen bir arama", "YALNIZCA" };
            foreach (var prefix in knownPrefixes)
            {
                if (rewritten.Contains(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    var nlIdx = rewritten.LastIndexOf('\n');
                    rewritten = nlIdx >= 0 ? rewritten[(nlIdx + 1)..].Trim() : question;
                    break;
                }
            }

            return rewritten.Length > 0 ? rewritten : question;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[RewriteQuery] Sorgu yeniden yazılamadı — soru: {Question}", question);
            return question;
        }
    }

    public async Task<string?> ValidateCachedAnswerAsync(
        string question,
        string cachedQuestion,
        string cachedAnswer,
        IEnumerable<(string Role, string Content)>? history = null,
        CancellationToken ct = default)
    {
        var system =
            "Sen bir cevap doğrulayıcısın. Sana bir soru, bu soruyla eşleştirilmiş önceki bir soru ve o soruya verilmiş cevap gösterilecek.\n" +
            "İki sorunun aynı şeyi kastetip kastetmediğine ve cevabın mevcut soruyu karşılayıp karşılamadığına karar ver.\n\n" +
            "Geçerli say (→ GECERLI):\n" +
            "• İki soru aynı konuyu/özneyi soruyor (kısaltma farkı, yazım farkı, sıralama farkı dahil)\n" +
            "• Cevap mevcut sorunun talebini doğrudan karşılıyor\n\n" +
            "Geçersiz say (→ GECERSIZ):\n" +
            "• Sorular farklı ürün/konu/özne hakkında\n" +
            "• Cevap mevcut soruyu kısmen karşılıyor ama eksik ya da yanlış bilgi içeriyor\n" +
            "• Konuşma geçmişine göre mevcut soru farklı bir şey kastediyor\n\n" +
            "YALNIZCA \"GECERLI\" veya \"GECERSIZ\" yaz. Başka hiçbir şey yazma.";

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

        var user =
            $"{historySection}" +
            $"MEVCUT SORU: {question}\n\n" +
            $"ÖNCEKİ SORU (cevabın yazıldığı): {cachedQuestion}\n\n" +
            $"CEVAP: {cachedAnswer[..Math.Min(500, cachedAnswer.Length)]}\n\n" +
            "Bu cevap mevcut soruyu karşılıyor mu? (GECERLI / GECERSIZ):";

        var payload = new
        {
            model = _helperModel,
            max_tokens = 10,
            temperature = 0.0f,
            messages = new object[]
            {
                new { role = "system", content = system },
                new { role = "user",   content = user }
            }
        };

        try
        {
            var response = await PostHelperAsync(payload, ct);
            if (!response.IsSuccessStatusCode) return null;

            var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
            var answer = json.GetProperty("choices")[0].GetProperty("message")
                             .GetProperty("content").GetString()?.Trim().ToUpperInvariant() ?? "";

            _logger.LogDebug("[CacheValidate] Soru='{Question}' → '{Answer}'", question, answer);

            // Türkçe karakterleri ASCII'ye indirgeyip exact karşılaştır:
            // "GEÇERLI", "gecerli", "Geçerli" hepsi → "GECERLI"; "GECERSIZ" / başka şey → null.
            var normalized = answer
                .Replace("Ç", "C").Replace("Ğ", "G").Replace("İ", "I")
                .Replace("Ö", "O").Replace("Ş", "S").Replace("Ü", "U");
            return normalized == "GECERLI" ? cachedAnswer : null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[CacheValidate] Kontrol başarısız, fresh cevap üretiliyor");
            return null;
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

    public async Task<string?> GenerateDocumentSummaryAsync(
        string sampleContent,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(sampleContent)) return null;

        // Çok uzun içeriği kısalt (token tasarrufu) — 4000 karakter yeterli özet için
        var truncated = sampleContent.Length > 4000
            ? sampleContent[..4000]
            : sampleContent;

        var system =
            "Sen bir belge özetleyicisin. Sana bir belgenin ilk bölümleri verilecek.\n" +
            "GÖREVİN: Belgenin KONUSUNU 1 cümlede (en fazla 150 karakter) özetle.\n" +
            "KURALLAR:\n" +
            "• Sadece konu/içerik özetini yaz, başka bir şey yazma\n" +
            "• Türkçe yaz\n" +
            "• \"Bu belge...\" gibi giriş yapma, doğrudan konu yaz\n" +
            "• Örnek: \"Çalışan izin, mesai ve maaş politikaları\"\n" +
            "• Örnek: \"TÜBITAK 1001 proje başvuru süreçleri ve şartları\"";

        var payload = new
        {
            model = _helperModel,
            max_tokens = 80,
            temperature = 0.2f,
            messages = new object[]
            {
                new { role = "system", content = system },
                new { role = "user",   content = "BELGE İÇERİĞİ:\n" + truncated }
            }
        };

        try
        {
            var response = await PostHelperAsync(payload, ct);
            if (!response.IsSuccessStatusCode) return null;
            var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
            var summary = json.GetProperty("choices")[0]
                              .GetProperty("message")
                              .GetProperty("content")
                              .GetString()?.Trim().Trim('"', '\'') ?? "";

            if (string.IsNullOrWhiteSpace(summary)) return null;

            // 200 karakter limit (DB field max)
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

        // Chunk içeriklerini özetle (her birinden ilk 300 karakter)
        var chunkList = chunks.Take(8).ToList();
        var chunksText = string.Join("\n\n", chunkList.Select((c, i) =>
            $"[CHUNK {i + 1} - {c.FileName}]\n{(c.Content.Length > 300 ? c.Content[..300] + "..." : c.Content)}"));

        var system =
            "Sen bir cevap kalite denetçisisin. Verilen soru, kaynak belge parçaları ve üretilen cevabı incele.\n\n" +
            "YAKLAŞIMIN: Cevaba güvenmeye çalış. False positive (gerçek sorun yokken alarm) maliyetli — kullanıcı güvenini sarsar.\n\n" +
            "YÜKSEK SKOR (0.8-1.0) ver eğer:\n" +
            "• Cevap soruyu net karşılıyor (kısa olabilir, sorun değil)\n" +
            "• İddialar kaynak parçalarda doğrulanabiliyor (kelimesi kelimesine değil, anlam olarak)\n" +
            "• Sayı/isim/tarih (varsa) tutarlı\n" +
            "• Eksik küçük detaylar varsa bile özü doğru\n\n" +
            "DÜŞÜK SKOR (< 0.5) sadece şu durumlarda:\n" +
            "• Cevap kaynak parçalarda OLMAYAN bilgi UYDURUYOR (halüsinasyon)\n" +
            "• Sayı/tarih/isim kaynaktan FARKLI (2024 sorusu için 1899 cevabı gibi)\n" +
            "• Cevap soruyla TAMAMEN alakasız\n" +
            "• Cevap kendi içinde ÇELİŞİYOR\n\n" +
            "ORTA SKOR (0.5-0.8) ise:\n" +
            "• Cevap kısmen eksik ama doğru → 0.7-0.8\n" +
            "• Stilistik kaygı veya marjinal şüphe → 0.7+\n\n" +
            "ÇIKTI (sadece JSON, başka metin YOK):\n" +
            "{\"score\": 0.0-1.0, \"issues\": [\"sorun1\", ...]}\n\n" +
            "Hiç sorun yoksa: {\"score\": 1.0, \"issues\": []}\n" +
            "Şüphede YÜKSEK skor ver (güven varsayılan).\n" +
            "Issue listesi sadece SOMUT problem için — \"daha fazla detay olsa iyi olur\" gibi yorumlar yazma.";

        var user =
            $"SORU: {question}\n\n" +
            $"KAYNAK PARÇALAR:\n{chunksText}\n\n" +
            $"ÜRETİLEN CEVAP:\n{(answer.Length > 1500 ? answer[..1500] + "..." : answer)}\n\n" +
            "Bu cevabın kalitesini JSON ile değerlendir:";

        var payload = new
        {
            model = _helperModel,
            max_tokens = 250,
            temperature = 0.0f,
            messages = new object[]
            {
                new { role = "system", content = system },
                new { role = "user",   content = user }
            }
        };

        try
        {
            var response = await PostHelperAsync(payload, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("[AnswerQuality] LLM çağrısı başarısız ({Status}) — fail-open Good() döndürülüyor", response.StatusCode);
                return AnswerQualityResult.Good();
            }

            var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
            var raw = json.GetProperty("choices")[0]
                          .GetProperty("message")
                          .GetProperty("content")
                          .GetString()?.Trim() ?? "";

            // LLM bazen markdown code fence kullanır: ```json {...} ```
            raw = System.Text.RegularExpressions.Regex.Replace(raw, @"^```(?:json)?\s*", "");
            raw = System.Text.RegularExpressions.Regex.Replace(raw, @"\s*```$", "");

            // JSON parse
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
            _logger.LogWarning(ex, "[AnswerQuality] Parse/çağrı hatası — fail-open Good() döndürülüyor");
            return AnswerQualityResult.Good();  // Validation çökerse asıl cevabı engelleme
        }
    }
}