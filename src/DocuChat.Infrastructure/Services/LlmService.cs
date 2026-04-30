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
            return "Sisteme yuklenm is belgeler arasinda bu soruyla ilgili bilgi bulunamadi.";

        var totalImageCount = 0;
        var chunksByDoc = chunkList
            .Select((c, i) => new { Chunk = c, Index = i })
            .GroupBy(x => x.Chunk.FileName);

        var contextParts = new List<string>();
        var globalChunkIdx = 0;

        foreach (var docGroup in chunksByDoc)
        {
            var docChunks = docGroup.ToList();
            var docParts = new List<string>();

            foreach (var item in docChunks)
            {
                var c = item.Chunk;
                var cleanContent = System.Text.RegularExpressions.Regex.Replace(
                    c.Content.Trim(), @"\[RESIM:[^\]]*\]", "").Trim();

                string chunkText;
                if (!string.IsNullOrWhiteSpace(c.ImagePath))
                {
                    List<string>? paths = null;
                    try { paths = JsonSerializer.Deserialize<List<string>>(c.ImagePath); } catch { }
                    var count = paths?.Count ?? 1;
                    var nums = string.Join(", ", Enumerable.Range(totalImageCount + 1, count).Select(n => $"[IMG:{n}]"));
                    Console.WriteLine($"[LLM Context] Parca {globalChunkIdx + 1} - {count} gorsel: {nums}");
                    totalImageCount += count;
                    chunkText = $"[PARCA {globalChunkIdx + 1}]\n[GORSELLER: {count} adet gorsel - {nums}]\n\n{cleanContent}";
                }
                else
                {
                    chunkText = $"[PARCA {globalChunkIdx + 1}]\n\n{cleanContent}";
                }
                docParts.Add(chunkText);
                globalChunkIdx++;
            }

            contextParts.Add($"=== BELGE: {docGroup.Key} ({docChunks.Count} parca) ===\n\n" + string.Join("\n\n---\n\n", docParts));
        }

        var context = string.Join("\n\n====================\n\n", contextParts);

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

        var systemPrompt =
            "Sen kurumsal belge analizi konusunda uzmanlasmis ileri duzey bir yapay zeka asistanisin.\n" +
            "Gorev: Kullanicinin sorusunu YALNIZCA sana verilen belge parcalarina dayanarak yanitla.\n\n" +
            "SORU ANALIZI:\n" +
            "Cevap vermeden once soruyu analiz et:\n" +
            "- Kullanici tam olarak ne istiyor? Tek bilgi mi, liste mi, tablo mu?\n" +
            "- Soru spesifik mi yoksa genel mi?\n" +
            "- Kullanici hepsini ver, tum liste, tam tablo gibi ifadeler kullandi mi?\n" +
            "Spesifik soru: Sadece o bilgiyi ver, tum tabloyu dokme.\n" +
            "Genel soru: Ilgili tum bilgiyi eksiksiz ver.\n\n" +
            "ALAKASIZ SORU KURALI - EN YUKSEK ONCELIK:\n" +
            "Asagidaki durumlarda yanit VERME, sadece su cumlelerden birini yaz:\n" +
            "- Soru yuklenen belgelerle hic ilgisi yoksa: Bu soru yuklenen belgelerle ilgili degil. Lutfen belge icerikleriyle ilgili bir soru sorun.\n" +
            "- Soru anlamsiz veya rastgele karakterler iceriyorsa: Anlasilan bir soru tespit edilemedi. Lutfen sorunuzu daha net ifade edin.\n" +
            "- Soru gunluk sohbet, selamlasma veya genel bilgi istegiyse: Ben yalnizca yuklenen belgeler hakkinda soru cevaplayabilirim.\n" +
            "Bu kurali kesinlikle atlatma. Belge parcalari verilmis olsa bile alakasiz sorulara YANIT VERME.\n\n" +
            "KAYNAK KURALLARI:\n" +
            "- YALNIZCA verilen PARCA bloklarindaki bilgiyi kullan.\n" +
            "- Belgelerde yazmayan hicbir bilgiyi ekleme, tahmin etme, tamamlama.\n" +
            "- Kendi genel bilginden hicbir sey uretme.\n" +
            "- Bilgi parcalarda yoksa: Bu bilgi yuklu belgelerde yer almiyor.\n" +
            "- Birden fazla parcada bilgi varsa hepsini tara ve birlestir.\n\n" +
            "BELGE SECIMI:\n" +
            "- Sana birden fazla belgeden parcalar gelebilir.\n" +
            "- Her belge === BELGE: dosyaadi === basligi altinda gruplu gelir.\n" +
            "- Soru tek belgeyle ilgiliyse YALNIZCA o belgenin parcalarini kullan.\n" +
            "- Soru birden fazla belgeyle ilgiliyse her ikisinden de ilgili bilgiyi al ve kaynagini belirt.\n" +
            "- Alakasiz belgenin icerigini ASLA yanita karistirma.\n\n" +
            "DOGRULUK:\n" +
            "- Sayilar, kodlar, tarihler, olcular HIC DEGISTIRMEDEN aktar.\n" +
            "- Yuvarlama yapma. Belgede ne yaziyorsa onu yaz.\n" +
            "- Kullanici acikca istedigi zaman tek satir bile atlama.\n\n" +
            "GORSEL KULLANIM:\n" +
            "- GORSELLER notu olan parcalarda gorselleri mutlaka uygun yerlere yerlestir.\n" +
            "- Tabloda Resim sutunu varsa her satira sirayla gorsel koy: | 1 | [IMG:1] | Is Ayakkabisi |\n" +
            "- Paragrafta nesneden bahsediliyorsa yanina koy: Is ayakkabisi [IMG:1] kullanilir.\n" +
            "- GORSELLER notunu yanita gosterme, bu senin icin talimattir.\n" +
            "- Uydurma IMG numarasi yazma. Sadece gonderilen gorseller icin kullan.\n\n" +
            "YANIT FORMATI:\n" +
            "- Yanitini her zaman TURKCE ver.\n" +
            "- PARCA etiketlerini yanita asla gosterme.\n" +
            "- Kaynak belirtirken parantez icinde dosya adi yaz: (dosyaadi.pdf)\n" +
            "- Elbette, Tabii ki, Merhaba gibi dolgu cumleleri kullanma. Dogrudan yanitla.\n" +
            "- Yanita KESINLIKLE soruyu tekrar yazma. 'Kullanici X diye soruyor' gibi ifadeler kullanma.\n" +
            "- Yanita giris cumlesi olarak soruyu ozetleme veya tekrarlama. Direkt cevapla.\n" +
            "- Tablo icerigi varsa markdown tablo formatinda sun.\n" +
            "- Kod icerigi varsa kod blogunda sun.\n" +
            "- Yanit uzunsa bolum basliklari kullan. Ayni bilgiyi iki kez yazma.\n\n" +
            "KONUSMA GECMISI:\n" +
            "- Onceki konusma sana ONCEKI KONUSMA basligi altinda iletilir.\n" +
            "- O, bu, bahsettigin, soyledigin gibi zamirler kullanildiginda gecmisten anla.\n" +
            "- Devam et, daha fazla ver, digerleri gibi ifadelerde onceki yaniti devam ettir.\n" +
            "- Her yeni soruyu konusmanin bir parcasi olarak degerlendir.\n" +
            "- Gecmis yanitlerdaki bilgileri tekrar etme, sadece yeni bilgi ekle.";

        var historyPrefix = "";
        if (historyList.Any())
        {
            var histLines = historyList
                .Select(h => (h.Role.ToLower() == "user" ? "Kullanici" : "Asistan") + ": " + h.Content)
                .ToList();
            historyPrefix = "ONCEKI KONUSMA:\n" + string.Join("\n", histLines) + "\n\n---\n\n";
        }

        var userMessage =
            historyPrefix +
            "BELGE PARCALARI:\n" +
            "====================================================================\n" +
            context + "\n" +
            "====================================================================\n\n" +
            "SORU: " + question + "\n\n" +
            "ONEMLI TALIMATLAR:\n" +
            "1. Once soruyu ve onceki konusmay birlikte analiz et.\n" +
            "2. Hangi belge ilgiliyse YALNIZCA o belgenin parcalarini kullan.\n" +
            "3. Spesifik soru ise sadece o bilgiyi ver, tum icerigi dokme.\n" +
            "4. Genel soru ise ilgili tum bilgiyi eksiksiz ver.\n" +
            "5. Sadece belgelerdeki bilgiyi kullan, disaridan bilgi ekleme.\n" +
            "6. GORSELLER notu olan parcalarda gorselleri MUTLAKA uygun yerlere yerlestir.\n" +
            "7. PARCA etiketlerini yanita kullanma.\n" +
            "8. Turkce yanit ver.";

        return _cfg["Llm:Provider"] switch
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

        var historyText = string.Join("\n", history.TakeLast(6).Select(h => h.Role + ": " + h.Content));
        var docsText = string.Join("\n", docList.Select((d, i) => (i + 1) + ". " + d));

        var prompt =
            "Asagidaki sohbet gecmisi ve soruyu analiz et.\n" +
            "Hangi belgeler bu soruyla ilgili? Sadece belge isimlerini dondur, baska hicbir sey yazma.\n" +
            "Birden fazla belge ilgiliyse hepsini yaz. Ilgisiz belgeleri yazma.\n\n" +
            "MEVCUT BELGELER:\n" + docsText + "\n\n" +
            "SOHBET GECMISI:\n" + historyText + "\n\n" +
            "SORU: " + question + "\n\n" +
            "SADECE ilgili belge isimlerini virgülle ayirarak yaz. Ornek: belge1.pdf, belge2.pdf\n" +
            "Hicbir belge ilgili degilse bos birak.";

        try
        {
            var payload = new
            {
                model = _cfg["Llm:Model"],
                max_tokens = 100,
                temperature = 0.0f,
                messages = new[] { new { role = "user", content = prompt } }
            };
            var response = await _http.PostAsJsonAsync("/openai/v1/chat/completions", payload, ct);
            if (!response.IsSuccessStatusCode) return new List<string>();
            var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
            var answer = json.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString()?.Trim() ?? "";
            Console.WriteLine($"[DocDetect] Tespit edilen belgeler: {answer}");
            if (string.IsNullOrWhiteSpace(answer)) return new List<string>();
            return answer.Split(',').Select(d => d.Trim()).Where(d => !string.IsNullOrWhiteSpace(d)).ToList();
        }
        catch { return new List<string>(); }
    }

    private async Task<string> CallOpenAiWithVisionAsync(
        string system, string user, List<string> imageUrls, CancellationToken ct)
    {
        var maxTokens = int.TryParse(_cfg["Llm:MaxTokens"], out var t) ? t : 4096;

        var userContent = new List<object> { new { type = "text", text = user } };

        foreach (var imgPath in imageUrls)
        {
            try
            {
                var fullPath = Path.Combine("uploads", imgPath);
                if (!File.Exists(fullPath))
                {
                    Console.WriteLine($"[LLM] Resim bulunamadi: {fullPath}");
                    continue;
                }
                Console.WriteLine($"[LLM] Resim gonderiliyor: {imgPath}");
                var imgBytes = await File.ReadAllBytesAsync(fullPath, ct);
                var ext = imgPath.EndsWith(".jpg") || imgPath.EndsWith(".jpeg") ? "jpeg" : "png";
                var base64 = Convert.ToBase64String(imgBytes);
                userContent.Add(new { type = "image_url", image_url = new { url = $"data:image/{ext};base64,{base64}" } });
            }
            catch (Exception ex) { Console.WriteLine($"[LLM] Resim eklenemedi: {ex.Message}"); }
        }

        var msgList = new List<object>
        {
            new { role = "system", content = system },
            userContent.Count > 1
                ? (object)new { role = "user", content = userContent }
                : (object)new { role = "user", content = user }
        };

        var payload = new { model = _cfg["Llm:Model"], max_tokens = maxTokens, temperature = 0.1f, messages = msgList };
        var response = await _http.PostAsJsonAsync("/openai/v1/chat/completions", payload, ct);
        await EnsureSuccessAsync(response);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        return json.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString()!.Trim();
    }

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

    private static async Task EnsureSuccessAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return;
        var body = await response.Content.ReadAsStringAsync();
        throw new HttpRequestException($"LLM API hatasi [{(int)response.StatusCode}]: {body}", inner: null, statusCode: response.StatusCode);
    }
}