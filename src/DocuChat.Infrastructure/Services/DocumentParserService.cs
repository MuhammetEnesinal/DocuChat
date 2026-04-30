using CsvHelper;
using DocuChat.Application.Interfaces.Services;
using DocuChat.Domain.Enums;
using ExcelDataReader;
using MimeKit;
using NPOI.XWPF.UserModel;
using PDFtoImage;
using SkiaSharp;
using System.Data;
using System.Globalization;
using System.Text;
using System.Net.Http.Json;
using System.Text.Json;
using ClosedXML.Excel;
using UglyToad.PdfPig;

namespace DocuChat.Infrastructure.Services;

public class DocumentParserService : IDocumentParser
{
    private readonly int _chunkSize;
    private readonly int _overlap;
    private readonly string _groqApiKey;
    private readonly string _groqVisionModel;
    private readonly string _llamaParseApiKey;
    private readonly IFileStorage _fileStorage;

    private const int RenderDpi = 400;

    public DocumentParserService(
        Microsoft.Extensions.Configuration.IConfiguration cfg,
        IFileStorage fileStorage)
    {
        _chunkSize = int.Parse(cfg["Chunking:ChunkSize"] ?? "800");
        _overlap = int.Parse(cfg["Chunking:Overlap"] ?? "150");
        _groqApiKey = cfg["GroqVision:ApiKey"] ?? cfg["Llm:ApiKey"]
            ?? throw new InvalidOperationException("GroqVision:ApiKey yapılandırılmamış.");
        _groqVisionModel = cfg["GroqVision:Model"] ?? "meta-llama/llama-4-scout-17b-16e-instruct";
        _llamaParseApiKey = cfg["LlamaParse:ApiKey"]
            ?? throw new InvalidOperationException("LlamaParse:ApiKey yapılandırılmamış.");
        _fileStorage = fileStorage;
    }

    // ─────────────────────────────────────────────────────────────────────
    public IEnumerable<ParsedChunk> Parse(Stream stream, FileType fileType)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        return fileType switch
        {
            FileType.Docx => SemanticChunk(ExtractDocxViaLlamaParse(stream,
                "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                "document.docx")),

            FileType.Doc => SemanticChunk(ExtractDocViaLlamaParse(stream)),

            FileType.Pdf => ExtractPdfPages(stream)
                                .SelectMany(r => SemanticChunk(r.Text, r.ImagePath)),

            FileType.Xlsx => ChunkWithImages(ExtractXlsx(stream), ExtractXlsxImages(stream)),
            FileType.Csv => Chunk(ExtractCsv(stream)),
            _ => Chunk(ExtractTxt(stream)),
        };
    }

    // ── PDF — Groq Vision + PdfPig resimleri ─────────────────────────────
    private record PageResult(string Text, string? ImagePath);

    private IEnumerable<PageResult> ExtractPdfPages(Stream stream)
    {
        var pdfBytes = ReadAllBytes(stream);
        var pages = new List<PageResult>();

        using var ms = new MemoryStream(pdfBytes);
        var pageImageBitmaps = Conversion.ToImages(ms, options: new RenderOptions { Dpi = RenderDpi }).ToList();

        foreach (var bitmap in pageImageBitmaps)
        {
            try
            {
                string pageText = string.Empty;
                for (var attempt = 0; attempt < 2; attempt++)
                {
                    try
                    {
                        pageText = OcrPageWithGroqAsync(bitmap, _groqApiKey, _groqVisionModel)
                                       .GetAwaiter().GetResult();
                        if (!string.IsNullOrWhiteSpace(pageText)) break;
                    }
                    catch (Exception ex)
                    {
                        if (attempt == 0)
                        {
                            Console.WriteLine($"[OCR] Retry: {ex.Message}");
                            Thread.Sleep(10_000);
                        }
                        else throw;
                    }
                }

                if (!string.IsNullOrWhiteSpace(pageText) && !pageText.Contains("[BOŞ_SAYFA]"))
                    pages.Add(new PageResult(pageText, null));

                Thread.Sleep(2_000);
            }
            finally { bitmap.Dispose(); }
        }

        // PdfPig ile resimleri çıkar ve sayfalarla eşleştir
        pages = AttachPdfPigImages(pdfBytes, pages).GetAwaiter().GetResult();
        return pages;
    }

    private static async Task<string> OcrPageWithGroqAsync(SKBitmap bitmap, string apiKey, string model)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        var base64 = Convert.ToBase64String(data.ToArray());

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        http.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");

        var prompt = """
            Sen kurumsal belge OCR uzmanısın. Görevin bu PDF sayfasındaki TÜM içeriği eksiksiz ve yapısal olarak doğru çıkarmaktır.

            ════════════════════════════════════════════════════════════
            1. TEMEL KURALLAR
            ════════════════════════════════════════════════════════════
            • Sayfadaki HER kelimeyi, rakamı, sembolü çıkar. Hiçbir şey atlama.
            • Orijinal dili koru. Çeviri yapma.
            • Kendi yorum veya açıklamanı ekleme. Sadece belgede yazanı aktar.
            • Giriş/kapanış cümleleri yazma ("İşte içerik:" gibi).
            • Sayfa numarası, üstbilgi, altbilgi gibi tekrar eden öğeleri dahil etme.

            ════════════════════════════════════════════════════════════
            2. BAŞLIKLAR
            ════════════════════════════════════════════════════════════
            • Ana başlık → # Başlık
            • Bölüm başlığı → ## Başlık
            • Alt bölüm → ### Başlık
            • Küçük başlık → #### Başlık
            • Başlıktan önce ve sonra boş satır bırak.

            ════════════════════════════════════════════════════════════
            3. PARAGRAFLAR
            ════════════════════════════════════════════════════════════
            • Her paragraf ayrı satırda. Aralarında boş satır bırak.
            • İki paragrafı birleştirme.
            • Kalın metin → **metin**, italik → *metin*

            ════════════════════════════════════════════════════════════
            4. TABLOLAR — EN KRİTİK BÖLÜM
            ════════════════════════════════════════════════════════════
            TABLO ÇIKARMA ADIMLARI:
            Adım 1: Sayfadaki tüm tabloları tespit et.
            Adım 2: Her tablonun kaç sütunu olduğunu say.
            Adım 3: Her satırı soldan sağa, yukarıdan aşağıya oku.
            Adım 4: Aşağıdaki formatta yaz:

            | Sütun1 | Sütun2 | Sütun3 |
            |--------|--------|--------|
            | Değer  | Değer  | Değer  |

            TABLO KURALLARI — KESİN:
            • İlk satır MUTLAKA başlık satırı olsun.
            • İkinci satır MUTLAKA |---|---|---| ayırıcısı olsun.
            • Her satır tam olarak aynı sayıda | işareti içersin.
            • Boş hücre varsa boş bırak ama | işaretini koru: |  |
            • HER SATIRI YAZ — tek satır bile atlama.
            • HER SÜTUNU DAHİL ET — tek sütun bile atlama.
            • Birleşik hücre varsa değeri ilgili tüm satırlara yaz.
            • Tablo öncesi ve sonrası boş satır bırak.

            ════════════════════════════════════════════════════════════
            5. LİSTELER
            ════════════════════════════════════════════════════════════
            • Sırasız liste: - madde (her madde yeni satırda)
            • Sıralı liste: 1. madde, 2. madde
            • Alt madde: iki boşluk girintisi
            • Liste öğelerini tek satırda birleştirme.

            ════════════════════════════════════════════════════════════
            6. TEKNİK İÇERİK
            ════════════════════════════════════════════════════════════
            • Model numaraları, kodlar, ölçüler — birebir kopyala.
            • Formüller, matematiksel ifadeler — olduğu gibi aktar.
            • Tarihler, yüzdeler, para birimleri — değiştirme.

            ════════════════════════════════════════════════════════════
            7. RESİMLER
            ════════════════════════════════════════════════════════════
            • Sayfada resim, fotoğraf, grafik varsa YOKSAY — sadece metni çıkar.
            • Resim altındaki/yanındaki metin açıklamalarını yaz.
            • Resimler ayrıca işlenecektir.

            ════════════════════════════════════════════════════════════
            8. ÇIKTI KALİTESİ
            ════════════════════════════════════════════════════════════
            • Çıktıyı okuyan kişi belgeyi hiç görmemiş olsun — yine de tüm bilgiyi anlasın.
            • Sayfada okunabilir metin yoksa SADECE şunu yaz: [BOŞ_SAYFA]
            • Asla "belge içeriği mevcut değil" gibi açıklama yazma.
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
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"[OCR] HTTP {(int)response.StatusCode}: {body}");

        var json = JsonSerializer.Deserialize<JsonElement>(body);
        return json.GetProperty("choices")[0]
                   .GetProperty("message")
                   .GetProperty("content")
                   .GetString()?.Trim() ?? string.Empty;
    }

    private async Task<List<PageResult>> AttachPdfPigImages(byte[] pdfBytes, List<PageResult> pages)
    {
        using var doc = PdfDocument.Open(pdfBytes);
        for (var pageNum = 1; pageNum <= doc.NumberOfPages && pageNum <= pages.Count; pageNum++)
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
                byte[]? imgBytes = null;
                if (img.TryGetPng(out var pngBytes)) imgBytes = pngBytes;
                else if (img.TryGetBytesAsMemory(out var mem)) imgBytes = mem.ToArray();
                else if (img.RawMemory.Length > 0) imgBytes = img.RawMemory.ToArray();
                if (imgBytes == null || imgBytes.Length < 10) continue;

                var ext = imgBytes.Length >= 2 && imgBytes[0] == 0xFF && imgBytes[1] == 0xD8 ? "jpg" : "png";
                using var imgStream = new MemoryStream(imgBytes);
                var savedPath = await _fileStorage.SaveAsync(imgStream, $"img_{Guid.NewGuid()}.{ext}");
                imagePaths.Add(savedPath);
                Console.WriteLine($"[PdfPig] Sayfa {pageNum}: resim -> {savedPath}");
            }

            if (imagePaths.Count > 0)
            {
                var existing = pages[pageNum - 1];
                pages[pageNum - 1] = new PageResult(existing.Text, JsonSerializer.Serialize(imagePaths));
            }
        }
        return pages;
    }

    // ── DOCX — MHTML tespiti → HTML parse, yoksa LlamaParse + XWPFDocument ──
    private string ExtractDocxViaLlamaParse(Stream stream, string contentType, string fileName)
    {
        stream.Position = 0;
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        stream.Position = 0;
        var rawBytes = ms.ToArray();

        // MHTML mi? (.docx uzantılı ama Confluence/Word HTML export olabilir)
        var header = System.Text.Encoding.ASCII.GetString(rawBytes, 0, Math.Min(20, rawBytes.Length));
        if (header.StartsWith("Message-ID:") || header.StartsWith("MIME-Version:"))
        {
            Console.WriteLine("[DOCX] MHTML formatı tespit edildi, HTML parser kullanılıyor.");
            return ExtractMhtml(rawBytes);
        }

        // Gerçek .docx (ZIP/OpenXML)
        // 1. Resimleri XWPFDocument ile çıkar
        var imagePaths = ExtractDocxImages(stream);
        stream.Position = 0;

        // 2. Metni LlamaParse ile al
        var text = CallLlamaParseAsync(stream, contentType, fileName).GetAwaiter().GetResult();
        stream.Position = 0;

        // 3. Resim path'lerini metne ekle
        if (imagePaths.Any())
            text += "\n[DOCX_IMAGES:" + JsonSerializer.Serialize(imagePaths) + "]";

        return text;
    }

    private List<string> ExtractDocxImages(Stream stream)
    {
        var imagePaths = new List<string>();
        stream.Position = 0;
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        ms.Position = 0;
        stream.Position = 0;

        // ZIP magic byte kontrolü — gerçek DOCX (OpenXML) mi?
        var magic = ms.ToArray();
        if (magic.Length < 4 || magic[0] != 0x50 || magic[1] != 0x4B)
        {
            Console.WriteLine("[DOCX] ZIP formatı değil, resim çıkartma atlanıyor.");
            return imagePaths;
        }

        ms.Position = 0;
        var doc = new XWPFDocument(ms);
        foreach (var pic in doc.AllPictures)
        {
            var ext = pic.SuggestFileExtension().ToLower();
            if (!new[] { "jpg", "jpeg", "png", "gif", "bmp" }.Contains(ext)) ext = "png";
            using var imgStream = new MemoryStream(pic.Data);
            var savedPath = _fileStorage.SaveAsync(imgStream, $"img_{Guid.NewGuid()}.{ext}").GetAwaiter().GetResult();
            imagePaths.Add(savedPath);
            Console.WriteLine($"[DOCX] Resim çıkartıldı: {savedPath}");
        }
        Console.WriteLine($"[DOCX] {imagePaths.Count} resim çıkartıldı.");
        return imagePaths;
    }

    // ── DOC — MHTML tespiti → HTML parse, yoksa LlamaParse ─────────────────
    private string ExtractDocViaLlamaParse(Stream stream)
    {
        stream.Position = 0;
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        stream.Position = 0;
        var rawBytes = ms.ToArray();

        // MHTML mi? (Confluence export formatı)
        var header = System.Text.Encoding.ASCII.GetString(rawBytes, 0, Math.Min(20, rawBytes.Length));
        if (header.StartsWith("Message-ID:") || header.StartsWith("MIME-Version:"))
        {
            Console.WriteLine("[DOC] MHTML formatı tespit edildi, HTML parser kullanılıyor.");
            return ExtractMhtml(rawBytes);
        }

        // Gerçek binary .doc — LlamaParse
        var imagePaths = ExtractDocImages(stream);
        stream.Position = 0;
        var text = CallLlamaParseAsync(stream, "application/msword", "document.doc").GetAwaiter().GetResult();
        stream.Position = 0;
        if (imagePaths.Any())
            text += "\n[DOCX_IMAGES:" + JsonSerializer.Serialize(imagePaths) + "]";
        return text;
    }

    private string ExtractMhtml(byte[] rawBytes)
    {
        var sb = new StringBuilder();
        var imagePaths = new List<string>();

        var msg = MimeMessage.Load(new MemoryStream(rawBytes));

        foreach (var part in msg.BodyParts)
        {
            if (part is MimeKit.MimePart mimePart)
            {
                var ct = mimePart.ContentType.MimeType.ToLower();

                if (ct == "text/html")
                {
                    using var bodyStream = new MemoryStream();
                    mimePart.Content.DecodeTo(bodyStream);
                    var html = System.Text.Encoding.UTF8.GetString(bodyStream.ToArray());
                    sb.Append(ExtractTextFromHtml(html));
                }
                else if (ct.StartsWith("image/") || ct == "application/octet-stream")
                {
                    try
                    {
                        using var imgStream = new MemoryStream();
                        mimePart.Content.DecodeTo(imgStream);
                        var imgBytes = imgStream.ToArray();
                        if (imgBytes.Length < 512) continue;

                        // Format tespit
                        string ext;
                        if (imgBytes[0] == 0xFF && imgBytes[1] == 0xD8) ext = "jpg";
                        else if (imgBytes[0] == 0x89 && imgBytes[1] == 0x50) ext = "png";
                        else if (imgBytes[0] == 0x47 && imgBytes[1] == 0x49) ext = "gif";
                        else ext = "png"; // varsayılan

                        imgStream.Position = 0;
                        var savedPath = _fileStorage.SaveAsync(imgStream, $"img_{Guid.NewGuid()}.{ext}").GetAwaiter().GetResult();
                        imagePaths.Add(savedPath);
                        Console.WriteLine($"[MHTML] Resim kaydedildi: {savedPath}");
                    }
                    catch (Exception ex) { Console.WriteLine($"[MHTML] Resim hatası: {ex.Message}"); }
                }
            }
        }

        if (imagePaths.Any())
            sb.Append("\n[DOCX_IMAGES:" + JsonSerializer.Serialize(imagePaths) + "]");

        Console.WriteLine($"[MHTML] {sb.Length} karakter, {imagePaths.Count} resim çıkarıldı.");
        return sb.ToString();
    }

    private static string ExtractTextFromHtml(string html)
    {
        var opts = System.Text.RegularExpressions.RegexOptions.IgnoreCase
                 | System.Text.RegularExpressions.RegexOptions.Singleline;

        html = System.Text.RegularExpressions.Regex.Replace(html, @"<head[^>]*>.*?</head>", "", opts);
        html = System.Text.RegularExpressions.Regex.Replace(html, @"<xml[^>]*>.*?</xml>", "", opts);
        html = System.Text.RegularExpressions.Regex.Replace(html, @"<style[^>]*>.*?</style>", "", opts);
        html = System.Text.RegularExpressions.Regex.Replace(html, @"<script[^>]*>.*?</script>", "", opts);

        html = System.Text.RegularExpressions.Regex.Replace(html, @"<h1[^>]*>(.*?)</h1>",
            m => "\n# " + StripTags(m.Groups[1].Value).Trim() + "\n", opts);
        html = System.Text.RegularExpressions.Regex.Replace(html, @"<h2[^>]*>(.*?)</h2>",
            m => "\n## " + StripTags(m.Groups[1].Value).Trim() + "\n", opts);
        html = System.Text.RegularExpressions.Regex.Replace(html, @"<h3[^>]*>(.*?)</h3>",
            m => "\n### " + StripTags(m.Groups[1].Value).Trim() + "\n", opts);
        html = System.Text.RegularExpressions.Regex.Replace(html, @"<h[456][^>]*>(.*?)</h[456]>",
            m => "\n#### " + StripTags(m.Groups[1].Value).Trim() + "\n", opts);

        html = System.Text.RegularExpressions.Regex.Replace(html, @"<li[^>]*>(.*?)</li>",
            m => "\n- " + StripTags(m.Groups[1].Value).Trim(), opts);

        html = System.Text.RegularExpressions.Regex.Replace(html, @"<tr[^>]*>(.*?)</tr>", m =>
        {
            var cells = System.Text.RegularExpressions.Regex.Matches(
                m.Groups[1].Value, @"<t[dh][^>]*>(.*?)</t[dh]>", opts);
            var cellTexts = cells.Cast<System.Text.RegularExpressions.Match>()
                .Select(c => StripTags(c.Groups[1].Value).Trim())
                .Where(t => !string.IsNullOrWhiteSpace(t)).ToList();
            if (cellTexts.Count == 0) return "";
            if (cellTexts.Count == 1) return "\n" + cellTexts[0];
            return "\n| " + string.Join(" | ", cellTexts) + " |";
        }, opts);

        html = System.Text.RegularExpressions.Regex.Replace(html, @"<br\s*/?>", "\n",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        html = System.Text.RegularExpressions.Regex.Replace(html, @"</p>|</div>", "\n",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        html = StripTags(html);
        html = System.Net.WebUtility.HtmlDecode(html);
        html = System.Text.RegularExpressions.Regex.Replace(html, @"[ \t]+", " ");
        html = System.Text.RegularExpressions.Regex.Replace(html, @"\n{3,}", "\n\n");

        // Yarıda kesilmiş satırları birleştir
        var rawLines = html.Split('\n');
        var merged = new List<string>();
        for (int i = 0; i < rawLines.Length; i++)
        {
            var line = rawLines[i].Trim();
            if (string.IsNullOrWhiteSpace(line)) { merged.Add(""); continue; }
            while (i + 1 < rawLines.Length)
            {
                var next = rawLines[i + 1].Trim();
                if (string.IsNullOrWhiteSpace(next)) break;
                if (next.StartsWith("#") || next.StartsWith("|") || next.StartsWith("- ") || next.StartsWith("  ")) break;
                if (line.EndsWith(".") || line.EndsWith("!") || line.EndsWith("?") || line.EndsWith(":")) break;
                if (next.Length > 0 && char.IsUpper(next[0])) break;
                line = line + " " + next;
                i++;
            }
            merged.Add(line);
        }
        return string.Join("\n", merged).Trim();
    }
    private static string StripTags(string html)
        => System.Text.RegularExpressions.Regex.Replace(html, @"<[^>]+>", " ");

    private List<string> ExtractDocImages(Stream stream)
    {
        var imagePaths = new List<string>();
        stream.Position = 0;
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        stream.Position = 0;

        var data = ms.ToArray();
        var i = 0;
        while (i < data.Length - 4)
        {
            // JPEG: FFD8FF
            if (data[i] == 0xFF && data[i + 1] == 0xD8 && data[i + 2] == 0xFF)
            {
                var end = FindJpegEnd(data, i);
                if (end > i + 512)
                {
                    var path = SaveImageBytes(data[i..end], "jpg");
                    if (path != null) imagePaths.Add(path);
                    i = end; continue;
                }
            }
            // PNG: 89504E47
            if (data[i] == 0x89 && data[i + 1] == 0x50 && data[i + 2] == 0x4E && data[i + 3] == 0x47)
            {
                var end = FindPngEnd(data, i);
                if (end > i + 512)
                {
                    var path = SaveImageBytes(data[i..end], "png");
                    if (path != null) imagePaths.Add(path);
                    i = end; continue;
                }
            }
            i++;
        }
        Console.WriteLine($"[DOC] Binary scan: {imagePaths.Count} resim bulundu.");
        return imagePaths;
    }

    private static int FindJpegEnd(byte[] data, int start)
    {
        for (var i = start + 2; i < data.Length - 1; i++)
            if (data[i] == 0xFF && data[i + 1] == 0xD9) return i + 2;
        return data.Length;
    }

    private static int FindPngEnd(byte[] data, int start)
    {
        for (var i = start + 8; i < data.Length - 7; i++)
            if (data[i] == 0x49 && data[i + 1] == 0x45 && data[i + 2] == 0x4E && data[i + 3] == 0x44)
                return i + 8;
        return data.Length;
    }

    private string? SaveImageBytes(byte[] imgBytes, string ext)
    {
        try
        {
            using var imgStream = new MemoryStream(imgBytes);
            var path = _fileStorage.SaveAsync(imgStream, $"img_{Guid.NewGuid()}.{ext}").GetAwaiter().GetResult();
            Console.WriteLine($"[DOC] Resim kaydedildi: {path}");
            return path;
        }
        catch (Exception ex) { Console.WriteLine($"[DOC] Resim kaydetme hatası: {ex.Message}"); return null; }
    }

    // ── LlamaParse ortak metot ────────────────────────────────────────────
    private async Task<string> CallLlamaParseAsync(Stream stream, string contentType, string fileName)
    {
        stream.Position = 0;
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms);
        ms.Position = 0;

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
        http.DefaultRequestHeaders.Add("Authorization", $"Bearer {_llamaParseApiKey}");

        var instruction =
            "GÖREV: Bu belgedeki TÜM içeriği eksiksiz, yapısal olarak birebir aynı şekilde markdown formatında çıkar.\n\n" +

            "PARAGRAFLAR — EN KRİTİK KURAL:\n" +
            "- Her paragrafı BÜTÜN olarak yaz. Cümleyi asla yarıda kesme.\n" +
            "- Bir paragraf nokta, soru işareti veya ünlem ile bitmiyorsa sonraki satırla birleştir.\n" +
            "- Paragraflar arasında TAM BİR boş satır bırak.\n" +
            "- Birden fazla cümle aynı konuyu işliyorsa aynı paragrafta tut.\n\n" +

            "BAŞLIKLAR:\n" +
            "- Ana başlık → # Başlık\n" +
            "- Bölüm başlığı → ## Başlık\n" +
            "- Alt bölüm → ### Başlık\n" +
            "- Küçük başlık → #### Başlık\n" +
            "- Başlıktan önce ve sonra boş satır bırak.\n\n" +

            "LİSTELER:\n" +
            "- Sırasız liste öğesi → - madde\n" +
            "- Sıralı liste öğesi → 1. madde, 2. madde ...\n" +
            "- Alt madde → iki boşluk girintisi\n" +
            "- Her madde kendi satırında. Maddeleri birleştirme.\n\n" +

            "TABLOLAR:\n" +
            "- Markdown tablo formatı: | Sütun1 | Sütun2 |\n" +
            "- İkinci satır MUTLAKA ayırıcı: |---|---|\n" +
            "- Her satır aynı sayıda sütun. Boş hücre → |  |\n" +
            "- Tablonun tüm satırlarını yaz, atlama.\n\n" +

            "ATLA:\n" +
            "- Sayfa numarası, üstbilgi, altbilgi\n" +
            "- CSS, JavaScript, HTML tag, Base64\n" +
            "- 'Belge içeriği mevcut değil' gibi açıklama cümleleri\n" +
            "- Placeholder içerik (Başlık 1, Veri 1 gibi)\n\n" +

            "GENEL:\n" +
            "- Belgede gerçekten var olanı yaz. Uydurma, tahmin etme.\n" +
            "- Giriş veya kapanış cümlesi ekleme. Direkt içeriği ver.\n" +
            "- Orijinal dili koru, çeviri yapma.";

        using var uploadForm = new MultipartFormDataContent();
        var fileBytes = new ByteArrayContent(ms.ToArray());
        fileBytes.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        uploadForm.Add(fileBytes, "file", fileName);
        uploadForm.Add(new StringContent(instruction), "parsing_instruction");

        var uploadResp = await http.PostAsync("https://api.cloud.llamaindex.ai/api/v1/parsing/upload", uploadForm);
        var uploadBody = await uploadResp.Content.ReadAsStringAsync();
        if (!uploadResp.IsSuccessStatusCode)
            throw new HttpRequestException($"[LlamaParse] Upload HTTP {(int)uploadResp.StatusCode}: {uploadBody}");

        var uploadJson = JsonSerializer.Deserialize<JsonElement>(uploadBody);
        var jobId = uploadJson.GetProperty("id").GetString();
        Console.WriteLine($"[LlamaParse] Job ID: {jobId}");

        for (var i = 0; i < 60; i++)
        {
            await Task.Delay(2_000);
            var statusResp = await http.GetAsync($"https://api.cloud.llamaindex.ai/api/v1/parsing/job/{jobId}");
            var statusJson = JsonSerializer.Deserialize<JsonElement>(await statusResp.Content.ReadAsStringAsync());
            var status = statusJson.TryGetProperty("status", out var s) ? s.GetString() : "";
            Console.WriteLine($"[LlamaParse] Status: {status}");
            if (status == "SUCCESS") break;
            if (status == "ERROR") throw new InvalidOperationException("[LlamaParse] İşlem başarısız.");
        }

        var resultResp = await http.GetAsync($"https://api.cloud.llamaindex.ai/api/v1/parsing/job/{jobId}/result/markdown");
        var resultBody = await resultResp.Content.ReadAsStringAsync();
        if (!resultResp.IsSuccessStatusCode)
            throw new HttpRequestException($"[LlamaParse] Result HTTP {(int)resultResp.StatusCode}: {resultBody}");

        var resultJson = JsonSerializer.Deserialize<JsonElement>(resultBody);
        var markdown = resultJson.TryGetProperty("markdown", out var md) ? md.GetString() ?? "" : resultBody;
        markdown = markdown.Replace("**", "");
        Console.WriteLine($"[LlamaParse] Başarılı, {markdown.Length} karakter");
        return markdown;
    }

    // ── XLSX resim çıkarma ───────────────────────────────────────────────────
    private List<string> ExtractXlsxImages(Stream stream)
    {
        var imagePaths = new List<string>();
        try
        {
            stream.Position = 0;
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            ms.Position = 0;
            stream.Position = 0;

            using var workbook = new XLWorkbook(ms);
            foreach (var worksheet in workbook.Worksheets)
            {
                foreach (var picture in worksheet.Pictures)
                {
                    try
                    {
                        using var imgStream = new MemoryStream();
                        picture.ImageStream.CopyTo(imgStream);
                        imgStream.Position = 0;
                        var imgBytes = imgStream.ToArray();
                        if (imgBytes.Length < 512) continue;

                        // Magic byte ile format tespit — namespace bağımsız
                        string ext;
                        if (imgBytes[0] == 0xFF && imgBytes[1] == 0xD8) ext = "jpg";
                        else if (imgBytes[0] == 0x89 && imgBytes[1] == 0x50) ext = "png";
                        else if (imgBytes[0] == 0x47 && imgBytes[1] == 0x49) ext = "gif";
                        else if (imgBytes[0] == 0x42 && imgBytes[1] == 0x4D) ext = "bmp";
                        else ext = "png";

                        imgStream.Position = 0;
                        var savedPath = _fileStorage.SaveAsync(imgStream, $"img_{Guid.NewGuid()}.{ext}").GetAwaiter().GetResult();
                        imagePaths.Add(savedPath);
                        Console.WriteLine($"[XLSX] Resim çıkartıldı: {savedPath}");
                    }
                    catch (Exception ex) { Console.WriteLine($"[XLSX] Resim hatası: {ex.Message}"); }
                }
            }
            Console.WriteLine($"[XLSX] {imagePaths.Count} resim çıkartıldı.");
        }
        catch (Exception ex) { Console.WriteLine($"[XLSX] Resim çıkarma genel hata: {ex.Message}"); }
        return imagePaths;
    }

    // ── XLSX ──────────────────────────────────────────────────────────────
    private static string ExtractXlsx(Stream stream)
    {
        var sb = new StringBuilder();
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        ms.Position = 0;
        using var reader = ExcelReaderFactory.CreateReader(ms,
            new ExcelReaderConfiguration { FallbackEncoding = Encoding.UTF8 });
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

    // ── CSV ───────────────────────────────────────────────────────────────
    private static string ExtractCsv(Stream stream)
    {
        var sb = new StringBuilder();
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
        return sb.ToString();
    }

    // ── TXT ───────────────────────────────────────────────────────────────
    private static string ExtractTxt(Stream stream)
    {
        var enc = DetectEncoding(stream);
        stream.Position = 0;
        using var r = new StreamReader(stream, enc);
        return r.ReadToEnd();
    }

    // ── Encoding detect ───────────────────────────────────────────────────
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
    // Strateji: Başlık gelince buffer'a ekle ama yield etme.
    // İlk içerik satırı (paragraf/madde/liste) gelince "başlık + içerik" birlikte birikir.
    // Yeni # veya ## başlığı geldiğinde önce mevcut chunk'ı yield et, sonra yeni başlığı buffer'a al.
    // Böylece başlıklar asla tek başına chunk olmaz.
    private IEnumerable<ParsedChunk> SemanticChunk(string text, string? imagePath = null)
    {
        if (string.IsNullOrWhiteSpace(text)) yield break;

        var docxImagesMatch = System.Text.RegularExpressions.Regex.Match(
            text, @"\[DOCX_IMAGES:(\[.*?\])\]");
        if (docxImagesMatch.Success)
        {
            imagePath = docxImagesMatch.Groups[1].Value;
            text = text.Replace(docxImagesMatch.Value, "").Trim();
        }

        text = WrapMarkdownTables(text);
        var segments = SplitPreservingTables(text).ToList();
        var buffer = new StringBuilder();
        var pendingHeadings = new StringBuilder(); // henüz içerik gelmeyen başlıklar

        foreach (var segment in segments)
        {
            if (segment.StartsWith("[TABLO BAŞLANGIÇ]"))
            {
                // Tablo öncesinde birikmiş içerik varsa yield et
                if (buffer.Length > 0)
                {
                    var p = buffer.ToString().Trim();
                    if (!string.IsNullOrWhiteSpace(p) && !IsBinaryContent(p))
                        yield return new ParsedChunk(p, imagePath);
                    buffer.Clear();
                }
                // Pending başlıklar varsa tabloyla birleştir
                var tableContent = pendingHeadings.Length > 0
                    ? pendingHeadings.ToString().Trim() + "\n" + segment.Trim()
                    : segment.Trim();
                pendingHeadings.Clear();
                if (!string.IsNullOrWhiteSpace(tableContent))
                    yield return new ParsedChunk(tableContent, imagePath);
                continue;
            }

            foreach (var line in segment.Split('\n'))
            {
                var trimmed = line.Trim();
                if (trimmed == "---") { trimmed = ""; }

                var isHeading = trimmed.StartsWith("# ") || trimmed == "#"
                             || trimmed.StartsWith("## ") || trimmed == "##"
                             || trimmed.StartsWith("### ") || trimmed.StartsWith("#### ");

                if (isHeading)
                {
                    var isTopHeading = trimmed.StartsWith("# ") || trimmed == "#"
                                    || trimmed.StartsWith("## ") || trimmed == "##";

                    if (isTopHeading)
                    {
                        // Üst başlık geldi — mevcut birikmiş içeriği yield et
                        if (buffer.Length > 0)
                        {
                            var chunk = buffer.ToString().Trim();
                            if (!string.IsNullOrWhiteSpace(chunk) && !IsBinaryContent(chunk))
                                yield return new ParsedChunk(chunk, imagePath);
                            buffer.Clear();
                        }
                        // Pending başlıklar varsa buffer'a taşı (içerik olmadan kalmış)
                        // Ama önce yeni üst başlığı pending'e al
                        if (pendingHeadings.Length > 0)
                            pendingHeadings.AppendLine();
                        pendingHeadings.AppendLine(trimmed);
                    }
                    else
                    {
                        // Alt başlık (###, ####) — pending'e ekle
                        pendingHeadings.AppendLine(trimmed);
                    }
                    continue;
                }

                if (string.IsNullOrWhiteSpace(trimmed))
                {
                    if (buffer.Length > 0) buffer.AppendLine();
                    continue;
                }

                // İçerik satırı — pending başlıkları buffer'a taşı
                if (pendingHeadings.Length > 0)
                {
                    buffer.Append(pendingHeadings);
                    pendingHeadings.Clear();
                }

                // Chunk boyutu kontrolü
                if (buffer.Length + trimmed.Length + 2 > _chunkSize * 1.5)
                {
                    var bufferText = buffer.ToString().Trim();
                    var splitPoint = FindLastSentenceEnd(bufferText);
                    if (splitPoint > _chunkSize / 2)
                    {
                        var chunkText = bufferText[..splitPoint].Trim();
                        if (!string.IsNullOrWhiteSpace(chunkText) && !IsBinaryContent(chunkText))
                            yield return new ParsedChunk(chunkText, imagePath);
                        var remainder = bufferText[splitPoint..].Trim();
                        buffer.Clear();
                        if (!string.IsNullOrWhiteSpace(remainder))
                            buffer.AppendLine(remainder);
                    }
                    else if (bufferText.Length > _chunkSize)
                    {
                        if (!string.IsNullOrWhiteSpace(bufferText) && !IsBinaryContent(bufferText))
                            yield return new ParsedChunk(bufferText, imagePath);
                        buffer.Clear();
                    }
                }

                buffer.AppendLine(trimmed);
            }
        }

        // Kalan içeriği yield et
        if (pendingHeadings.Length > 0 && buffer.Length == 0)
        {
            // Sadece başlık kalmış, içerik yok — yine de yield et
            var headingOnly = pendingHeadings.ToString().Trim();
            if (!string.IsNullOrWhiteSpace(headingOnly))
                yield return new ParsedChunk(headingOnly, imagePath);
        }
        else
        {
            if (pendingHeadings.Length > 0)
                buffer.Insert(0, pendingHeadings.ToString());
            var last = buffer.ToString().Trim();
            if (!string.IsNullOrWhiteSpace(last) && !IsBinaryContent(last))
                yield return new ParsedChunk(last, imagePath);
        }
    }

    // ── ChunkWithImages (XLSX) ───────────────────────────────────────────────
    private IEnumerable<ParsedChunk> ChunkWithImages(string text, List<string> imagePaths)
    {
        var imagePath = imagePaths.Any() ? JsonSerializer.Serialize(imagePaths) : null;
        if (string.IsNullOrWhiteSpace(text)) yield break;
        text = WrapMarkdownTables(text);
        var segments = SplitPreservingTables(text);
        var buffer = new StringBuilder();

        foreach (var segment in segments)
        {
            if (segment.StartsWith("[TABLO BAŞLANGIÇ]"))
            {
                if (buffer.Length > 0) { var p = buffer.ToString().Trim(); if (!string.IsNullOrWhiteSpace(p)) yield return new ParsedChunk(p, imagePath); buffer.Clear(); }
                yield return new ParsedChunk(segment.Trim(), imagePath);
                continue;
            }
            foreach (var line in segment.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var s = line.Trim();
                if (string.IsNullOrWhiteSpace(s)) continue;
                if (buffer.Length > 0 && buffer.Length + s.Length + 1 > _chunkSize)
                {
                    var chunk = buffer.ToString().Trim();
                    if (!string.IsNullOrWhiteSpace(chunk) && !IsBinaryContent(chunk)) yield return new ParsedChunk(chunk, imagePath);
                    var tail = chunk.Length > _overlap ? chunk[^_overlap..] : chunk;
                    buffer.Clear();
                    if (!string.IsNullOrWhiteSpace(tail)) buffer.Append(tail).Append(' ');
                }
                buffer.Append(s).Append(' ');
            }
        }
        var last = buffer.ToString().Trim();
        if (!string.IsNullOrWhiteSpace(last)) yield return new ParsedChunk(last, imagePath);
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
        var lower = text.ToLowerInvariant();
        if (lower.Contains("[boş_sayfa]")) return true;
        if (lower.Contains("belge içeriği mevcut değil")) return true;
        if (lower.Contains("display: none") || lower.Contains("position: relative") || lower.Contains("page-break")) return true;
        var words = text.Split(new char[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return true;
        if (words.Average(w => w.Length) > 20) return true;
        var allowed = new HashSet<char> { ' ', '\n', '\r', '\t', '.', ',', ';', ':', '!', '?', '(', ')', '-', '/', '_' };
        var nonAlpha = text.Count(c => !char.IsLetterOrDigit(c) && !allowed.Contains(c));
        return (double)nonAlpha / text.Length > 0.5;
    }

    // Son cümle sınırını bul (., !, ?) — chunk'ı cümle ortasında kesme
    private static int FindLastSentenceEnd(string text)
    {
        var sentenceEnders = new[] { ". ", "! ", "? " };
        var lastPos = -1;
        foreach (var ender in sentenceEnders)
        {
            var pos = text.LastIndexOf(ender, StringComparison.Ordinal);
            if (pos > lastPos) lastPos = pos + 1;
        }
        return lastPos > 0 ? lastPos : -1;
    }


    private static string WrapMarkdownTables(string text)
    {
        var lines = text.Split('\n');
        var result = new List<string>();
        var tableBuffer = new List<string>();
        var inMarked = false;
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