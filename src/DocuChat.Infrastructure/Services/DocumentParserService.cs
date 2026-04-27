using CsvHelper;
using DocuChat.Application.Interfaces.Services;
using DocuChat.Domain.Enums;
using ExcelDataReader;
using NPOI.XWPF.UserModel;
using PDFtoImage;
using SkiaSharp;
using System.Data;
using System.Globalization;
using System.Text;
using System.Net.Http.Json;
using System.Text.Json;
using Tesseract;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace DocuChat.Infrastructure.Services;

public class DocumentParserService : IDocumentParser
{
    private readonly int _chunkSize;
    private readonly int _overlap;
    private readonly string _tessDataPath;
    private readonly string _tessLang;
    private readonly string? _groqApiKey;
    private readonly string _groqVisionModel;
    private readonly string? _llamaParseApiKey;
    private readonly IFileStorage _fileStorage;

    private const int RenderDpi = 400;

    public DocumentParserService(
        Microsoft.Extensions.Configuration.IConfiguration cfg,
        IFileStorage fileStorage)
    {
        _chunkSize = int.Parse(cfg["Chunking:ChunkSize"] ?? "800");
        _overlap = int.Parse(cfg["Chunking:Overlap"] ?? "150");
        _tessDataPath = cfg["Tesseract:DataPath"] ?? @"C:\Users\bsstajyer\AppData\Local\Programs\Tesseract-OCR\tessdata";
        _tessLang = cfg["Tesseract:Language"] ?? "tur+eng";
        _groqApiKey = cfg["Llm:ApiKey"];
        _groqVisionModel = cfg["GroqVision:Model"] ?? "meta-llama/llama-4-scout-17b-16e-instruct";
        _llamaParseApiKey = cfg["LlamaParse:ApiKey"];
        _fileStorage = fileStorage;
    }

    public IEnumerable<ParsedChunk> Parse(Stream stream, FileType fileType)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        if (fileType is FileType.Docx or FileType.Doc)
        {
            var docText = fileType == FileType.Doc
                ? ExtractDocx(stream, "application/msword", "document.doc")
                : ExtractDocx(stream, "application/vnd.openxmlformats-officedocument.wordprocessingml.document", "document.docx");
            return SemanticChunk(docText);
        }

        if (fileType == FileType.Pdf)
            return ExtractPdfPages(stream).SelectMany(r => SemanticChunk(r.Text, r.ImagePath));

        var text = fileType switch
        {
            FileType.Xlsx => ExtractXlsx(stream),
            FileType.Csv => ExtractCsv(stream),
            _ => ExtractTxt(stream),
        };
        return Chunk(text);
    }

    // ── PDF ───────────────────────────────────────────────────────────────
    private record PageResult(string Text, string? ImagePath);

    private IEnumerable<PageResult> ExtractPdfPages(Stream stream)
    {
        var pdfBytes = ReadAllBytes(stream);
        var pages = new List<PageResult>();

        if (!string.IsNullOrWhiteSpace(_groqApiKey))
        {
            try
            {
                using var ms = new MemoryStream(pdfBytes);
                var pageImageBitmaps = Conversion.ToImages(ms, options: new RenderOptions { Dpi = RenderDpi }).ToList();
                var pageImages = pageImageBitmaps; // ikinci çağrı için sakla

                foreach (var bitmap in pageImages)
                {
                    try
                    {
                        string pageText = string.Empty;
                        for (var attempt = 0; attempt < 2; attempt++)
                        {
                            try
                            {
                                pageText = OcrPageWithGroqAsync(bitmap, _groqApiKey!, _groqVisionModel).GetAwaiter().GetResult();
                                if (!string.IsNullOrWhiteSpace(pageText)) break;
                            }
                            catch (Exception ex)
                            {
                                if (attempt == 0) { Console.WriteLine($"[OCR] Retry: {ex.Message}"); System.Threading.Thread.Sleep(3000); }
                            }
                        }
                        if (!string.IsNullOrWhiteSpace(pageText))
                            pages.Add(new PageResult(pageText, null));
                    }
                    finally { bitmap.Dispose(); }
                }

                if (pages.Count > 0)
                {
                    // PdfPig ile resimleri çıkar
                    var rawPages = ExtractImagesWithPdfPig(pdfBytes, pages).GetAwaiter().GetResult();
                    // Groq Vision ile resim açıklamalarını al ve eşleştir
                    pages = MatchImagesWithDescriptions(rawPages, pageImages, _groqApiKey!, _groqVisionModel).GetAwaiter().GetResult();
                    return pages;
                }
                Console.WriteLine("[OCR] Groq Vision metin döndürmedi, Tesseract fallback");
            }
            catch (Exception ex) { Console.WriteLine("[OCR] Groq Vision hata: " + ex.Message); }
        }

        // Tesseract fallback
        TesseractEngine? engine = null;
        try { engine = new TesseractEngine(_tessDataPath, _tessLang, EngineMode.LstmOnly); } catch { }
        try
        {
            using var ms2 = new MemoryStream(pdfBytes);
            var pageImages = Conversion.ToImages(ms2, options: new RenderOptions { Dpi = RenderDpi });
            foreach (var bitmap in pageImages)
            {
                try
                {
                    var pageText = engine is not null ? OcrPageWithTesseract(bitmap, engine) : string.Empty;
                    if (!string.IsNullOrWhiteSpace(pageText))
                        pages.Add(new PageResult(pageText, null));
                }
                finally { bitmap.Dispose(); }
            }
        }
        catch (Exception ex) { pages.Add(new PageResult($"[PDF okuma hatası: {ex.Message}]", null)); }
        finally { engine?.Dispose(); }
        return pages;
    }

    // ── Groq Vision ───────────────────────────────────────────────────────
    private static async Task<string> OcrPageWithGroqAsync(SKBitmap bitmap, string apiKey, string model)
    {
        try
        {
            using var image = SKImage.FromBitmap(bitmap);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            var base64 = Convert.ToBase64String(data.ToArray());

            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
            http.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");

            var prompt = """
                Sen kurumsal belge analizi konusunda uzmanlaşmış bir yapay zeka sistemsin.
                Görevin: Bu PDF sayfasındaki içeriği yapısal bütünlüğünü koruyarak eksiksiz çıkarmak.

                ═══════════════════════════════════════════════════
                TEMEL KURALLAR
                ═══════════════════════════════════════════════════
                ✦ Sayfadaki HER bilgiyi çıkar, hiçbir şeyi atlama.
                ✦ Orijinal dili koru, asla çeviri yapma.
                ✦ Sayfa numarası, üstbilgi, altbilgi dahil etme.
                ✦ Yorumlama yapma, sadece belgede yazanı aktar.

                ═══════════════════════════════════════════════════
                METİN VE PARAGRAF
                ═══════════════════════════════════════════════════
                ✦ Her paragrafı ayrı satır yaz, aralarında boş satır bırak.
                ✦ Kalın metinleri **böyle** işaretle.

                ═══════════════════════════════════════════════════
                BAŞLIKLAR
                ═══════════════════════════════════════════════════
                ✦ Ana başlıklar: ## Başlık
                ✦ Alt başlıklar: ### Alt Başlık

                ═══════════════════════════════════════════════════
                LİSTELER
                ═══════════════════════════════════════════════════
                ✦ Madde listeleri: - madde
                ✦ Numaralı listeler: 1. madde

                ═══════════════════════════════════════════════════
                TABLOLAR
                ═══════════════════════════════════════════════════
                ✦ Tablolar markdown formatında:
                  | Sütun1 | Sütun2 |
                  |--------|--------|
                  | Değer  | Değer  |
                ✦ Tüm satırları eksiksiz yaz.

                ═══════════════════════════════════════════════════
                RESİMLER — ÇOK ÖNEMLİ
                ═══════════════════════════════════════════════════
                ✦ Sayfada fotoğraf, resim, grafik, diyagram, şema görürsen:
                  Tam olarak göründüğü yere [R1] yaz. Sonraki resim [R2], sonraki [R3] ...
                ✦ Sayım 1den başlar, her resim için bir sonraki sayıyı kullan.
                ✦ Tablo hücresindeyse o hücreye yaz: | 1 | [R1] | Güvenli Falçata |
                ✦ Paragraf içindeyse yanına yaz: Şekil 1 [R1] açıklaması
                ✦ Logo, imza, arka plan deseni için kullanma.

                ═══════════════════════════════════════════════════
                ÇIKTI
                ═══════════════════════════════════════════════════
                ✦ Sadece içerik, yorum veya açıklama ekleme.
                """;

            var payload = new
            {
                model,
                max_tokens = 4096,
                temperature = 0,
                messages = new object[]
                {
                    new { role = "user", content = new object[]
                    {
                        new { type = "text", text = prompt },
                        new { type = "image_url", image_url = new { url = $"data:image/png;base64,{base64}" } }
                    }}
                }
            };

            var response = await http.PostAsJsonAsync("https://api.groq.com/openai/v1/chat/completions", payload);
            var body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode) { Console.WriteLine($"[OCR] HTTP {(int)response.StatusCode}: {body}"); return string.Empty; }

            var json = JsonSerializer.Deserialize<JsonElement>(body);
            if (!json.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0) return string.Empty;

            return choices[0].GetProperty("message").GetProperty("content").GetString()?.Trim() ?? string.Empty;
        }
        catch (Exception ex) { Console.WriteLine("[OCR] Groq Vision hata: " + ex.Message); return string.Empty; }
    }

    // ── PdfPig — gömülü resimleri çıkar ──────────────────────────────────
    // Groq Vision ile her sayfanın resimlerini açıkla, chunk'larla eşleştir
    private static async Task<List<PageResult>> MatchImagesWithDescriptions(
        List<PageResult> pages, List<SKBitmap> bitmaps, string apiKey, string model)
    {
        for (var i = 0; i < pages.Count && i < bitmaps.Count; i++)
        {
            var page = pages[i];

            // Bu sayfada imagePath (JSON array) var mı?
            if (string.IsNullOrWhiteSpace(page.ImagePath)) continue;

            List<string>? imagePaths;
            try { imagePaths = System.Text.Json.JsonSerializer.Deserialize<List<string>>(page.ImagePath); }
            catch { continue; }
            if (imagePaths == null || imagePaths.Count == 0) continue;

            try
            {
                // Groq Vision'a sayfayı gönder — resim açıklamalarını al
                using var image = SKImage.FromBitmap(bitmaps[i]);
                using var data = image.Encode(SKEncodedImageFormat.Png, 100);
                var base64 = Convert.ToBase64String(data.ToArray());

                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
                http.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");

                var count = imagePaths.Count;
                var prompt = "Bu sayfada " + count + " adet resim/fotograf var.\n" +
                    "Her birini yukari-asagi, sol-sag siraya gore acikla.\n" +
                    "SADECE su formatta cevap ver, baska hicbir sey yazma:\n" +
                    "1: resmin kisa aciklamasi\n" +
                    "2: resmin kisa aciklamasi\n" +
                    "Ornek:\n" +
                    "1: Guvenli falcata fotografi\n" +
                    "2: Makas fotografi\n" +
                    "3: Tornavida fotografi";

                var payload = new
                {
                    model,
                    max_tokens = 500,
                    temperature = 0,
                    messages = new object[]
                    {
                        new { role = "user", content = new object[]
                        {
                            new { type = "text", text = prompt },
                            new { type = "image_url", image_url = new { url = $"data:image/png;base64,{base64}" } }
                        }}
                    }
                };

                var response = await http.PostAsJsonAsync("https://api.groq.com/openai/v1/chat/completions", payload);
                var body = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode) continue;

                var json = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(body);
                if (!json.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0) continue;

                var descriptions = choices[0].GetProperty("message").GetProperty("content").GetString()?.Trim() ?? "";
                Console.WriteLine($"[Groq Vision] Sayfa {i + 1} resim aciklamalari: {descriptions}");

                // "1: açıklama" formatını parse et
                var descMap = new Dictionary<int, string>();
                foreach (var line in descriptions.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    var parts = line.Split(':', 2);
                    if (parts.Length == 2 && int.TryParse(parts[0].Trim(), out var num))
                        descMap[num] = parts[1].Trim();
                }

                // Açıklamaları page text'e ekle — her resim için "[Görsel N: açıklama]" formatında
                var updatedText = page.Text;
                for (var j = 0; j < imagePaths.Count; j++)
                {
                    var desc = descMap.TryGetValue(j + 1, out var d) ? d : $"Görsel {j + 1}";
                    updatedText += $"\n[Gorsel {j + 1}: {desc}]";
                }

                pages[i] = new PageResult(updatedText, page.ImagePath);
            }
            catch (Exception ex) { Console.WriteLine($"[Groq Vision] Resim açıklama hatası: {ex.Message}"); }
        }
        return pages;
    }

    private async Task<List<PageResult>> ExtractImagesWithPdfPig(byte[] pdfBytes, List<PageResult> pages)
    {
        try
        {
            using var doc = PdfDocument.Open(pdfBytes);
            var pageCount = doc.NumberOfPages;

            for (var pageNum = 1; pageNum <= pageCount && pageNum <= pages.Count; pageNum++)
            {
                var page = doc.GetPage(pageNum);
                var pageImages = page.GetImages()
                    .OrderByDescending(img => img.Bounds.Top)
                    .ThenBy(img => img.Bounds.Left)
                    .ToList();

                if (pageImages.Count == 0) continue;

                var imagePaths = new List<string>();
                foreach (var img in pageImages)
                {
                    try
                    {
                        byte[]? imgBytes = null;
                        if (img.TryGetPng(out var pngBytes)) imgBytes = pngBytes;
                        else if (img.TryGetBytesAsMemory(out var mem)) imgBytes = mem.ToArray();
                        else if (img.RawMemory.Length > 0) imgBytes = img.RawMemory.ToArray();

                        if (imgBytes == null || imgBytes.Length < 10) continue;

                        var ext = imgBytes.Length >= 2 && imgBytes[0] == 0xFF && imgBytes[1] == 0xD8 ? "jpg" : "png";
                        using var imgStream = new MemoryStream(imgBytes);
                        var savedPath = await _fileStorage.SaveAsync(imgStream, $"img_{Guid.NewGuid()}.{ext}");
                        imagePaths.Add(savedPath);
                        Console.WriteLine($"[PdfPig] Sayfa {pageNum}: resim {imagePaths.Count} -> {savedPath}");
                    }
                    catch (Exception ex) { Console.WriteLine($"[PdfPig] Hata: {ex.Message}"); }
                }

                if (imagePaths.Count > 0)
                {
                    var existing = pages[pageNum - 1];
                    pages[pageNum - 1] = new PageResult(existing.Text, JsonSerializer.Serialize(imagePaths));
                }
            }
        }
        catch (Exception ex) { Console.WriteLine($"[PdfPig] Hata: {ex.Message}"); }

        return pages;
    }

    private static string OcrPageWithTesseract(SKBitmap bitmap, TesseractEngine engine)
    {
        try
        {
            using var image = SKImage.FromBitmap(bitmap);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            using var pix = Pix.LoadFromMemory(data.ToArray());
            using var ocrPage = engine.Process(pix, PageSegMode.Auto);
            return ocrPage.GetText()?.Trim() ?? string.Empty;
        }
        catch { return string.Empty; }
    }

    // ── DOCX ─────────────────────────────────────────────────────────────
    private string ExtractDocx(Stream stream, string contentType, string fileName)
    {
        if (!string.IsNullOrWhiteSpace(_llamaParseApiKey))
        {
            try
            {
                var result = ExtractDocxViaLlamaParseAsync(stream, contentType, fileName).GetAwaiter().GetResult();
                if (!string.IsNullOrWhiteSpace(result)) return result;
            }
            catch (Exception ex) { Console.WriteLine($"[DOCX] LlamaParse hata, NPOI fallback: {ex.Message}"); }
        }
        return ExtractDocxViaNpoi(stream);
    }

    private async Task<string> ExtractDocxViaLlamaParseAsync(Stream stream, string contentType, string fileName)
    {
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        ms.Position = 0;

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
        http.DefaultRequestHeaders.Add("Authorization", $"Bearer {_llamaParseApiKey}");

        var instruction =
            "Belgedeki tüm anlamlı içeriği eksiksiz çıkar. " +
            "Tablolar varsa markdown formatında ver. " +
            "Madde listeleri için - işareti, numaralı listeler için 1. 2. 3. formatını kullan. " +
            "Ana başlıklar için ##, alt başlıklar için ### kullan. " +
            "Sayfa numarası, üstbilgi, altbilgi gibi tekrar eden öğeleri dahil etme. " +
            "CSS kodları, binary içerikler varsa tamamen atla. " +
            "Yalnızca belgenin asıl içeriğini — metin, tablo, liste — çıkar.";

        using var uploadForm = new MultipartFormDataContent();
        var fileBytes = new ByteArrayContent(ms.ToArray());
        fileBytes.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document");
        uploadForm.Add(fileBytes, "file", "document.docx");
        uploadForm.Add(new StringContent(instruction), "parsing_instruction");

        var uploadResp = await http.PostAsync("https://api.cloud.llamaindex.ai/api/v1/parsing/upload", uploadForm);
        var uploadBody = await uploadResp.Content.ReadAsStringAsync();
        if (!uploadResp.IsSuccessStatusCode) { Console.WriteLine($"[LlamaParse] Upload HTTP {(int)uploadResp.StatusCode}: {uploadBody}"); return string.Empty; }

        var uploadJson = JsonSerializer.Deserialize<JsonElement>(uploadBody);
        var jobId = uploadJson.GetProperty("id").GetString();
        Console.WriteLine($"[LlamaParse] Job ID: {jobId}");

        for (var i = 0; i < 60; i++)
        {
            await Task.Delay(2000);
            var statusResp = await http.GetAsync($"https://api.cloud.llamaindex.ai/api/v1/parsing/job/{jobId}");
            var statusBody = await statusResp.Content.ReadAsStringAsync();
            var statusJson = JsonSerializer.Deserialize<JsonElement>(statusBody);
            var status = statusJson.TryGetProperty("status", out var s) ? s.GetString() : "";
            Console.WriteLine($"[LlamaParse] Status: {status}");
            if (status == "SUCCESS") break;
            if (status == "ERROR") { Console.WriteLine("[LlamaParse] İşlem başarısız"); return string.Empty; }
        }

        var resultResp = await http.GetAsync($"https://api.cloud.llamaindex.ai/api/v1/parsing/job/{jobId}/result/markdown");
        var resultBody = await resultResp.Content.ReadAsStringAsync();
        if (!resultResp.IsSuccessStatusCode) { Console.WriteLine($"[LlamaParse] Result HTTP {(int)resultResp.StatusCode}: {resultBody}"); return string.Empty; }

        var resultJson = JsonSerializer.Deserialize<JsonElement>(resultBody);
        var markdown = resultJson.TryGetProperty("markdown", out var md) ? md.GetString() ?? "" : resultBody;
        markdown = markdown.Replace("**", "");

        var lines = markdown.Split('\n');
        var cleaned = new StringBuilder();
        foreach (var line in lines)
        {
            var trimmed = line.TrimStart();
            cleaned.AppendLine(trimmed.StartsWith("#") ? trimmed.TrimStart('#').Trim() : line);
        }
        markdown = cleaned.ToString();
        Console.WriteLine($"[LlamaParse] Başarılı, {markdown.Length} karakter");
        return markdown;
    }

    private static string ExtractDocxViaNpoi(Stream stream)
    {
        var sb = new StringBuilder();
        try
        {
            stream.Position = 0;
            var doc = new XWPFDocument(stream);
            foreach (var bodyElement in doc.BodyElements)
            {
                switch (bodyElement)
                {
                    case XWPFParagraph para:
                        var text = para.Text.Trim();
                        if (!string.IsNullOrWhiteSpace(text)) sb.AppendLine(text);
                        break;
                    case XWPFTable table:
                        sb.AppendLine("[TABLO BAŞLANGIÇ]");
                        var rows = table.Rows;
                        if (rows.Count == 0) break;
                        var headers = rows[0].GetTableCells().Select(c => c.GetText().Trim()).ToList();
                        if (headers.Any(h => !string.IsNullOrWhiteSpace(h)))
                            sb.AppendLine("Başlıklar: " + string.Join(" | ", headers));
                        foreach (var row in rows.Skip(1))
                        {
                            var cells = row.GetTableCells().Select(c => c.GetText().Trim()).ToList();
                            var parts = new List<string>();
                            for (var i = 0; i < cells.Count; i++)
                            {
                                if (string.IsNullOrWhiteSpace(cells[i])) continue;
                                var h = i < headers.Count && !string.IsNullOrWhiteSpace(headers[i]) ? headers[i] : $"Sütun{i + 1}";
                                parts.Add($"{h}: {cells[i]}");
                            }
                            if (parts.Any()) sb.AppendLine(string.Join(", ", parts));
                        }
                        sb.AppendLine("[TABLO BİTİŞ]");
                        break;
                }
            }
        }
        catch (Exception ex) { Console.WriteLine($"[DOCX] NPOI hata: {ex.Message}"); }
        return sb.ToString();
    }

    // ── XLSX ──────────────────────────────────────────────────────────────
    private static string ExtractXlsx(Stream stream)
    {
        var sb = new StringBuilder();
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            ms.Position = 0;
            using var reader = ExcelReaderFactory.CreateReader(ms, new ExcelReaderConfiguration { FallbackEncoding = Encoding.UTF8 });
            var dataSet = reader.AsDataSet(new ExcelDataSetConfiguration
            {
                ConfigureDataTable = _ => new ExcelDataTableConfiguration { UseHeaderRow = false }
            });
            foreach (DataTable table in dataSet.Tables)
            {
                if (table.Rows.Count == 0) continue;
                var headerRowIdx = 0; var maxFilled = 0;
                for (var ri = 0; ri < Math.Min(5, table.Rows.Count); ri++)
                {
                    var filled = table.Rows[ri].ItemArray.Count(c => c != null && !string.IsNullOrWhiteSpace(c.ToString()));
                    if (filled > maxFilled) { maxFilled = filled; headerRowIdx = ri; }
                }
                var headers = new List<string>(); var lastHeader = string.Empty;
                for (var col = 0; col < table.Columns.Count; col++)
                {
                    var val = table.Rows[headerRowIdx][col]?.ToString()?.Trim() ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(val)) val = lastHeader; else lastHeader = val;
                    headers.Add(val);
                }
                var headerLine = "Başlıklar: " + string.Join(" | ", headers.Where(h => !string.IsNullOrWhiteSpace(h)));
                const int chunkRows = 50;
                var batch = new List<string>();
                for (var ri = headerRowIdx + 1; ri < table.Rows.Count; ri++)
                {
                    var row = table.Rows[ri]; var parts = new List<string>(); var lastVal = string.Empty;
                    for (var ci = 0; ci < table.Columns.Count; ci++)
                    {
                        var val = row[ci]?.ToString()?.Trim() ?? string.Empty;
                        if (string.IsNullOrWhiteSpace(val)) val = lastVal; else lastVal = val;
                        if (string.IsNullOrWhiteSpace(val)) continue;
                        var header = ci < headers.Count && !string.IsNullOrWhiteSpace(headers[ci]) ? headers[ci] : $"Sütun{ci + 1}";
                        parts.Add($"{header}: {val}");
                    }
                    if (parts.Any()) batch.Add(string.Join(", ", parts));
                    if (batch.Count >= chunkRows) { FlushBatch(sb, table.TableName, headerLine, batch); batch.Clear(); }
                }
                if (batch.Any()) FlushBatch(sb, table.TableName, headerLine, batch);
            }
        }
        catch (Exception ex) { Console.WriteLine($"[XLSX] Hata: {ex.Message}"); }
        return sb.ToString();
    }

    private static void FlushBatch(StringBuilder sb, string sheetName, string headerLine, List<string> batch)
    {
        sb.AppendLine("[TABLO BAŞLANGIÇ]");
        sb.AppendLine($"Sayfa: {sheetName}");
        sb.AppendLine(headerLine);
        foreach (var r in batch) sb.AppendLine(r);
        sb.AppendLine("[TABLO BİTİŞ]");
    }

    // ── CSV ──────────────────────────────────────────────────────────────
    private static string ExtractCsv(Stream stream)
    {
        var sb = new StringBuilder();
        try
        {
            var enc = DetectEncoding(stream);
            stream.Position = 0;
            using var peekMs = new MemoryStream(ReadAllBytes(stream));
            using var peek = new StreamReader(peekMs, enc);
            var firstLine = peek.ReadLine() ?? string.Empty;
            var delimiter = firstLine.Count(c => c == ';') > firstLine.Count(c => c == ',') ? ';' : ',';
            stream.Position = 0;
            var cfg = new CsvHelper.Configuration.CsvConfiguration(CultureInfo.InvariantCulture)
            {
                Delimiter = delimiter.ToString(),
                BadDataFound = null,
                MissingFieldFound = null
            };
            using var reader = new StreamReader(stream, enc);
            using var csv = new CsvReader(reader, cfg);
            var headers = new List<string>(); var headerLine = string.Empty;
            var batch = new List<string>(); const int chunkRows = 50; var isFirst = true;
            while (csv.Read())
            {
                if (isFirst)
                {
                    headers = Enumerable.Range(0, csv.Parser.Count).Select(i => csv.GetField(i) ?? string.Empty).ToList();
                    headerLine = "Başlıklar: " + string.Join(" | ", headers);
                    isFirst = false; continue;
                }
                var parts = new List<string>();
                for (var i = 0; i < csv.Parser.Count; i++)
                {
                    var val = csv.GetField(i) ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(val)) continue;
                    parts.Add($"{(i < headers.Count ? headers[i] : $"Sütun{i + 1}")}: {val}");
                }
                if (parts.Any()) batch.Add(string.Join(", ", parts));
                if (batch.Count >= chunkRows) { FlushBatch(sb, "CSV", headerLine, batch); batch.Clear(); }
            }
            if (batch.Any()) FlushBatch(sb, "CSV", headerLine, batch);
        }
        catch (Exception ex) { Console.WriteLine($"[CSV] Hata: {ex.Message}"); }
        return sb.ToString();
    }

    // ── TXT ──────────────────────────────────────────────────────────────
    private static string ExtractTxt(Stream stream)
    {
        try { var enc = DetectEncoding(stream); stream.Position = 0; using var r = new StreamReader(stream, enc); return r.ReadToEnd(); }
        catch { return string.Empty; }
    }

    // ── Encoding ─────────────────────────────────────────────────────────
    private static Encoding DetectEncoding(Stream stream)
    {
        stream.Position = 0;
        Span<byte> bom = stackalloc byte[4];
        var read = 0; int b;
        while (read < 4 && (b = stream.ReadByte()) != -1) bom[read++] = (byte)b;
        if (read >= 3 && bom[0] == 0xEF && bom[1] == 0xBB && bom[2] == 0xBF) return new UTF8Encoding(true);
        if (read >= 2 && bom[0] == 0xFF && bom[1] == 0xFE) return Encoding.Unicode;
        if (read >= 2 && bom[0] == 0xFE && bom[1] == 0xFF) return Encoding.BigEndianUnicode;
        stream.Position = 0;
        var sample = new byte[Math.Min(stream.Length, 8192)]; var totalRead = 0; int bytesRead;
        while (totalRead < sample.Length && (bytesRead = stream.Read(sample, totalRead, sample.Length - totalRead)) > 0) totalRead += bytesRead;
        try { if (!Encoding.UTF8.GetString(sample, 0, totalRead).Contains('\uFFFD')) return Encoding.UTF8; } catch { }
        return Encoding.UTF8;
    }

    private static byte[] ReadAllBytes(Stream stream)
    {
        stream.Position = 0;
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    // ── SemanticChunk ─────────────────────────────────────────────────────
    private IEnumerable<ParsedChunk> SemanticChunk(string text, string? imagePath = null)
    {
        if (string.IsNullOrWhiteSpace(text)) yield break;
        text = WrapMarkdownTables(text);
        var segments = SplitPreservingTables(text).ToList();
        var buffer = new StringBuilder();

        foreach (var segment in segments)
        {
            if (segment.StartsWith("[TABLO BAŞLANGIÇ]"))
            {
                if (buffer.Length > 0)
                {
                    var p = buffer.ToString().Trim();
                    if (!string.IsNullOrWhiteSpace(p) && !IsBinaryContent(p))
                        yield return new ParsedChunk(p, imagePath);
                    else if (!string.IsNullOrWhiteSpace(p))
                    {
                        yield return new ParsedChunk(p + "\n" + segment.Trim(), imagePath);
                        buffer.Clear();
                        continue;
                    }
                    buffer.Clear();
                }
                yield return new ParsedChunk(segment.Trim(), imagePath);
                continue;
            }

            foreach (var line in segment.Split('\n'))
            {
                var trimmed = line.Trim();
                if (string.IsNullOrWhiteSpace(trimmed)) continue;

                var isSectionBreak = trimmed == "---"
                    || trimmed.StartsWith("# ") || trimmed.StartsWith("## ") || trimmed.StartsWith("### ");

                if (isSectionBreak && buffer.Length > 0)
                {
                    var chunk = buffer.ToString().Trim();
                    if (!string.IsNullOrWhiteSpace(chunk) && !IsBinaryContent(chunk))
                        yield return new ParsedChunk(chunk, imagePath);
                    buffer.Clear();
                    if (trimmed != "---") buffer.AppendLine(trimmed);
                    continue;
                }

                if (buffer.Length > 0 && buffer.Length + trimmed.Length + 1 > _chunkSize)
                {
                    var chunk = buffer.ToString().Trim();
                    if (!string.IsNullOrWhiteSpace(chunk) && !IsBinaryContent(chunk))
                        yield return new ParsedChunk(chunk, imagePath);
                    var tail = chunk.Length > _overlap ? chunk[^_overlap..] : chunk;
                    buffer.Clear();
                    if (!string.IsNullOrWhiteSpace(tail)) buffer.Append(tail).Append(' ');
                }
                buffer.AppendLine(trimmed);
            }
        }

        var last = buffer.ToString().Trim();
        if (!string.IsNullOrWhiteSpace(last) && !IsBinaryContent(last))
            yield return new ParsedChunk(last, imagePath);
    }

    // ── Chunk (XLSX/CSV/TXT) ──────────────────────────────────────────────
    private IEnumerable<ParsedChunk> Chunk(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) yield break;
        text = WrapMarkdownTables(text);
        var segments = SplitPreservingTables(text);
        var buffer = new StringBuilder();

        foreach (var segment in segments)
        {
            if (segment.StartsWith("[TABLO BAŞLANGIÇ]"))
            {
                if (buffer.Length > 0) { var p = buffer.ToString().Trim(); if (!string.IsNullOrWhiteSpace(p)) yield return new ParsedChunk(p); buffer.Clear(); }
                yield return new ParsedChunk(segment.Trim());
                continue;
            }
            foreach (var line in segment.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var s = line.Trim();
                if (string.IsNullOrWhiteSpace(s)) continue;
                if (buffer.Length > 0 && buffer.Length + s.Length + 1 > _chunkSize)
                {
                    var chunk = buffer.ToString().Trim();
                    if (!string.IsNullOrWhiteSpace(chunk) && !IsBinaryContent(chunk)) yield return new ParsedChunk(chunk);
                    var tail = chunk.Length > _overlap ? chunk[^_overlap..] : chunk;
                    buffer.Clear();
                    if (!string.IsNullOrWhiteSpace(tail)) buffer.Append(tail).Append(' ');
                }
                buffer.Append(s).Append(' ');
            }
        }
        var last = buffer.ToString().Trim();
        if (!string.IsNullOrWhiteSpace(last)) yield return new ParsedChunk(last);
    }

    private static bool IsBinaryContent(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length < 50) return false;
        var words = text.Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return true;
        if (words.Average(w => w.Length) > 20) return true;
        var nonAlpha = text.Count(c => !char.IsLetterOrDigit(c) && c != ' ' && c != '\n' && c != '\r' && c != '\t'
            && c != '.' && c != ',' && c != ';' && c != ':' && c != '!' && c != '?' && c != '(' && c != ')' && c != '-' && c != '/' && c != '_');
        return (double)nonAlpha / text.Length > 0.5;
    }

    private static string WrapMarkdownTables(string text)
    {
        var lines = text.Split('\n'); var result = new List<string>(); var tableBuffer = new List<string>(); var inMarked = false;
        foreach (var line in lines)
        {
            var t = line.Trim();
            if (t == "[TABLO BAŞLANGIÇ]") { inMarked = true; result.Add(t); continue; }
            if (t == "[TABLO BİTİŞ]") { inMarked = false; result.Add(t); continue; }
            if (inMarked) { result.Add(t); continue; }
            if (t.StartsWith("|")) { tableBuffer.Add(t); }
            else
            {
                if (tableBuffer.Count > 0) { result.Add("[TABLO BAŞLANGIÇ]"); result.AddRange(tableBuffer); result.Add("[TABLO BİTİŞ]"); tableBuffer.Clear(); }
                result.Add(t);
            }
        }
        if (tableBuffer.Count > 0) { result.Add("[TABLO BAŞLANGIÇ]"); result.AddRange(tableBuffer); result.Add("[TABLO BİTİŞ]"); }
        return string.Join("\n", result);
    }

    private static IEnumerable<string> SplitPreservingTables(string text)
    {
        var start = 0;
        while (start < text.Length)
        {
            var tableStart = text.IndexOf("[TABLO BAŞLANGIÇ]", start, StringComparison.Ordinal);
            if (tableStart < 0) { var remaining = text[start..]; if (!string.IsNullOrWhiteSpace(remaining)) yield return remaining; yield break; }
            if (tableStart > start) { var before = text[start..tableStart]; if (!string.IsNullOrWhiteSpace(before)) yield return before; }
            var tableEnd = text.IndexOf("[TABLO BİTİŞ]", tableStart, StringComparison.Ordinal);
            if (tableEnd < 0) { var rest = text[tableStart..]; if (!string.IsNullOrWhiteSpace(rest)) yield return rest; yield break; }
            tableEnd += "[TABLO BİTİŞ]".Length;
            yield return text[tableStart..tableEnd];
            start = tableEnd;
        }
    }
}