using System.Diagnostics;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using DocuChat.Application.Interfaces.Services;
using DocuChat.Domain.Enums;
using DocuChat.Infrastructure.Services.Documents.Parsing.Ast;
using DocuChat.Infrastructure.Services.Documents.Parsing.Chunking;
using DocuChat.Infrastructure.Services.Documents.Parsing.Linking;
using DocuChat.Infrastructure.Services.Documents.Parsing.Models;
using DocuChat.Infrastructure.Services.Documents.Parsing.Rendering;
using DocuChat.Infrastructure.Services.Documents.Parsing.StructuredExtractors;
using DocuChat.Application.ServiceContracts;
using DocumentFormat.OpenXml.Packaging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using UglyToad.PdfPig;
using A = DocumentFormat.OpenXml.Drawing;

namespace DocuChat.Infrastructure.Services.Documents;

public class DocumentParserService : IDocumentParser
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly IFileStorage _fileStorage;
    private readonly ILogger<DocumentParserService> _logger;
    private readonly string _mistralModel;
    private readonly int _maxTokens;
    private readonly string _sofficePath;

    private readonly MarkdownBlockExtractor _blockExtractor;
    private readonly BlockMerger _blockMerger;
    private readonly ImageLinker _imageLinker;
    private readonly IMarkdownRenderer _renderer;
    private readonly SemanticChunker _chunker;

    public DocumentParserService(
        IConfiguration cfg,
        IFileStorage fileStorage,
        IHttpClientFactory httpFactory,
        IEmbeddingService embedder,
        ILogger<DocumentParserService> logger)
    {
        _httpFactory = httpFactory;
        _fileStorage = fileStorage;
        _logger = logger;
        _mistralModel = cfg["Mistral:Model"] ?? "mistral-ocr-latest";
        _maxTokens = int.TryParse(cfg["Chunking:MaxTokens"], out var mt) ? mt : 800;
        _sofficePath = cfg["LibreOffice:Path"] ?? "soffice";

        var tokens = new TokenCounter();
        _blockExtractor = new MarkdownBlockExtractor(new TableExtractor());
        _blockMerger = new BlockMerger();
        _imageLinker = new ImageLinker();
        _renderer = new MarkdownRenderer();
        var splitter = new SemanticSplitter(embedder, tokens);
        _chunker = new SemanticChunker(tokens, _renderer, splitter);
    }

    public async Task<IEnumerable<ParsedChunk>> ParseAsync(Stream stream, FileType fileType)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        var bytes = await ReadAllBytesAsync(stream);

        // DOCX / DOC: LibreOffice ile PDF'e çevir → PDF akışına yönlendir
        if (fileType is FileType.Docx or FileType.Doc)
        {
            var ext = fileType == FileType.Docx ? "docx" : "doc";
            if (IsMhtml(bytes))
            {
                _logger.LogInformation("[Parser] MHTML formatı tespit edildi → MimeKit ile HTML'e çıkar");
                bytes = await ExtractHtmlFromMhtmlAsync(bytes);
                ext = "html";
            }
            bytes = await ConvertToPdfAsync(bytes, ext);
            fileType = FileType.Pdf;
        }

        var mime = MimeFor(fileType);

        // XLSX: belgeye [[EMBED_IMG_N]] placeholder'ları enjekte et, Mistral metne çevirsin
        List<string> placeholderPaths = new();
        if (fileType == FileType.Xlsx)
        {
            var prep = await PrepareXlsxWithPlaceholdersAsync(bytes);
            bytes = prep.Bytes;
            placeholderPaths = prep.Paths;
        }

        var mistralPages = await CallMistralAsync(bytes, mime);

        // PDF için sayfa-bazlı PdfPig fallback (Mistral'in kaçırdığı dijital embedded'ler)
        IReadOnlyList<List<string>> pdfEmbeddedPerPage = fileType == FileType.Pdf
            ? await ExtractPdfEmbeddedPerPageAsync(bytes)
            : Array.Empty<List<string>>();


        // [Pre] Her sayfa markdown'ında inline image referanslarını [IMG_PATH:...]'a çevir
        for (var i = 0; i < mistralPages.Count; i++)
        {
            var mp = mistralPages[i];
            var md = mp.Markdown;

            for (var local = 0; local < mp.FigurePaths.Count; local++)
                md = md.Replace($"[IMG_REF:{local}]", $"[IMG_PATH:{mp.FigurePaths[local]}]");

            md = Regex.Replace(md, @"\[\[EMBED_IMG_(\d+)\]\]", m =>
            {
                var n = int.Parse(m.Groups[1].Value);
                return n >= 0 && n < placeholderPaths.Count
                    ? $"[IMG_PATH:{placeholderPaths[n]}]"
                    : string.Empty;
            });

            mistralPages[i] = mp with { Markdown = md };
        }

        // [1] Her sayfa AST → SemanticBlock listesi
        var tracker = new HeaderChainTracker();
        var allBlocks = new List<SemanticBlock>();
        var globalIdx = 0;
        foreach (var mp in mistralPages.OrderBy(p => p.Index))
        {
            var pageBlocks = _blockExtractor.Extract(mp.Markdown, mp.Index + 1, tracker, ref globalIdx);
            allBlocks.AddRange(pageBlocks);
        }

        // [2] Sayfa sınırı bölünmesi (tablo/paragraf/liste)
        allBlocks = _blockMerger.Merge(allBlocks);

        // [3] PdfPig fallback resimleri Y-koordinatla ilgili block'a iliştir
        if (fileType == FileType.Pdf)
        {
            var fallbackImages = new List<ImageWithBbox>();
            for (var pageIdx = 0; pageIdx < pdfEmbeddedPerPage.Count; pageIdx++)
            {
                var pagePaths = pdfEmbeddedPerPage[pageIdx];
                for (var k = 0; k < pagePaths.Count; k++)
                {
                    var normY = pagePaths.Count == 1 ? 0.5 : (k + 0.5) / pagePaths.Count;
                    fallbackImages.Add(new ImageWithBbox(
                        pagePaths[k], pageIdx + 1, normY, 0.5, "PdfPig"));
                }
            }
            if (fallbackImages.Count > 0)
                _imageLinker.Link(allBlocks, fallbackImages);
            // Marker yerleştirme: MarkdownRenderer block.Images'ı tablo altına / paragraf sonuna append eder.
        }

        // [4] SemanticChunker — token bütçesi + tablo atomik + embedding-based semantic split + [IMG_PATH]→[IMG:N]
        var builtChunks = await _chunker.ChunkAsync(allBlocks, _maxTokens);

        // [5] Mini-chunk filtresi (anlamsız küçük parçalar)
        builtChunks = builtChunks.Where(c => (c.CleanContent?.Length ?? 0) >= 30).ToList();

        // [6] ContentHash + ParsedChunk dönüşümü
        var final = new List<ParsedChunk>(builtChunks.Count);
        foreach (var c in builtChunks)
        {
            var hash = ComputeContentHash(c.MarkdownContent);
            var imagePathJson = c.ImagePaths.Count > 0
                ? JsonSerializer.Serialize(c.ImagePaths)
                : null;

            final.Add(new ParsedChunk(
                Content: c.MarkdownContent,
                ImagePath: imagePathJson,
                Header: string.IsNullOrEmpty(c.Header) ? null : c.Header,
                CleanContent: c.CleanContent,
                PageNumber: c.PageNumber > 0 ? c.PageNumber : null,
                StructuredTableJson: c.StructuredTableJson,
                TokenCount: c.TokenCount,
                ContentHash: hash));
        }

        var totalImages = final.Sum(c => CountImagesInJson(c.ImagePath));
        _logger.LogInformation("[Parser] {FileType}: {Chunks} chunk ({Tables} tablo), {Imgs} resim",
            fileType, final.Count,
            final.Count(c => c.StructuredTableJson != null),
            totalImages);
        return final;
    }

    private static int CountImagesInJson(string? imagePathJson)
    {
        if (string.IsNullOrEmpty(imagePathJson)) return 0;
        try
        {
            using var doc = JsonDocument.Parse(imagePathJson);
            return doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement.GetArrayLength() : 0;
        }
        catch { return 0; }
    }

    private static string ComputeContentHash(string content)
    {
        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(content ?? string.Empty));
        return Convert.ToHexString(bytes);
    }

    private async Task<byte[]> ConvertToPdfAsync(byte[] sourceBytes, string sourceExt)
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), $"doc2pdf_{Guid.NewGuid()}");
        Directory.CreateDirectory(tmpDir);
        var inFile = Path.Combine(tmpDir, $"in.{sourceExt}");
        var outFile = Path.Combine(tmpDir, "in.pdf");

        try
        {
            await File.WriteAllBytesAsync(inFile, sourceBytes);

            // Her çağrıya ayrı UserInstallation profili ver — paralel çağrılar çakışmasın
            var userProfile = new Uri(Path.Combine(tmpDir, "lo_profile")).AbsoluteUri;

            var psi = new ProcessStartInfo
            {
                FileName = _sofficePath,
                Arguments = $"-env:UserInstallation={userProfile} --headless --convert-to pdf --outdir \"{tmpDir}\" \"{inFile}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi)
                ?? throw new InvalidOperationException("LibreOffice (soffice) başlatılamadı. Kurulu mu?");

            var stdout = await proc.StandardOutput.ReadToEndAsync();
            var stderr = await proc.StandardError.ReadToEndAsync();
            var timeoutMs = 120_000;
            var completed = proc.WaitForExit(timeoutMs);
            if (!completed)
            {
                try { proc.Kill(true); } catch { }
                throw new TimeoutException("LibreOffice 120 sn içinde tamamlanmadı.");
            }

            if (proc.ExitCode != 0)
                throw new InvalidOperationException(
                    $"LibreOffice exit {proc.ExitCode}: {stderr}");

            if (!File.Exists(outFile))
                throw new FileNotFoundException(
                    $"LibreOffice çıktı dosyası yok: {outFile}\nstdout: {stdout}\nstderr: {stderr}");

            var pdfBytes = await File.ReadAllBytesAsync(outFile);
            _logger.LogInformation("[LibreOffice] {Ext} → PDF: {InSize} → {OutSize} byte",
                sourceExt, sourceBytes.Length, pdfBytes.Length);
            return pdfBytes;
        }
        finally
        {
            try { Directory.Delete(tmpDir, recursive: true); } catch { }
        }
    }

    private record MistralPage(int Index, string Markdown, List<string> FigurePaths);

    private async Task<List<MistralPage>> CallMistralAsync(byte[] bytes, string mime)
    {
        var http = _httpFactory.CreateClient("Mistral");
        var dataUrl = $"data:{mime};base64,{Convert.ToBase64String(bytes)}";
        var payload = new
        {
            model = _mistralModel,
            document = new { type = "document_url", document_url = dataUrl },
            include_image_base64 = true
        };

        JsonDocument? json = null;
        for (var attempt = 1; attempt <= 4; attempt++)
        {
            try
            {
                using var resp = await http.PostAsJsonAsync("/v1/ocr", payload);
                if (!resp.IsSuccessStatusCode)
                {
                    var body = await resp.Content.ReadAsStringAsync();
                    _logger.LogWarning("[Mistral] HTTP {S} try {N}/4: {B}",
                        (int)resp.StatusCode, attempt, body.Length > 400 ? body[..400] : body);
                    if (attempt < 4 && (int)resp.StatusCode >= 500)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt - 1)));
                        continue;
                    }
                    resp.EnsureSuccessStatusCode();
                }
                json = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
                break;
            }
            catch (Exception ex) when (attempt < 4)
            {
                _logger.LogWarning("[Mistral] {T}: {M} retry {N}/4", ex.GetType().Name, ex.Message, attempt);
                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt - 1)));
            }
        }
        if (json is null) throw new InvalidOperationException("Mistral OCR yanıt vermedi.");

        var pages = new List<MistralPage>();
        if (!json.RootElement.TryGetProperty("pages", out var pagesEl) || pagesEl.ValueKind != JsonValueKind.Array)
            return pages;

        foreach (var pageEl in pagesEl.EnumerateArray())
        {
            var idx = pageEl.TryGetProperty("index", out var ie) && ie.TryGetInt32(out var i) ? i : pages.Count;
            var md = pageEl.TryGetProperty("markdown", out var me) ? (me.GetString() ?? "") : "";
            var figs = new List<string>();

            if (pageEl.TryGetProperty("images", out var imgsEl) && imgsEl.ValueKind == JsonValueKind.Array)
            {
                var localIdx = 0;
                foreach (var imgEl in imgsEl.EnumerateArray())
                {
                    var imgId = imgEl.TryGetProperty("id", out var ide) ? ide.GetString() : null;
                    var b64 = imgEl.TryGetProperty("image_base64", out var be) ? be.GetString() : null;
                    if (string.IsNullOrWhiteSpace(b64)) continue;
                    var c = b64.IndexOf(',');
                    if (c >= 0) b64 = b64[(c + 1)..];
                    byte[] imgBytes;
                    try { imgBytes = Convert.FromBase64String(b64); } catch { continue; }
                    if (imgBytes.Length < 64) continue;

                    var ext = imgBytes[0] == 0xFF && imgBytes[1] == 0xD8 ? "jpg" : "png";
                    using var ms = new MemoryStream(imgBytes);
                    var path = await _fileStorage.SaveRawAsync(ms, $"img_{Guid.NewGuid()}.{ext}");
                    figs.Add(path);

                    if (!string.IsNullOrEmpty(imgId))
                    {
                        var pattern = @"!\[[^\]]*\]\(" + Regex.Escape(imgId) + @"\)";
                        md = Regex.Replace(md, pattern, $"[IMG_REF:{localIdx}]");
                    }
                    localIdx++;
                }
            }
            pages.Add(new MistralPage(idx, md, figs));
        }
        _logger.LogInformation("[Mistral] {P} sayfa, {F} figure", pages.Count, pages.Sum(p => p.FigurePaths.Count));
        return pages;
    }

    private async Task<List<List<string>>> ExtractPdfEmbeddedPerPageAsync(byte[] pdfBytes)
    {
        var result = new List<List<string>>();
        try
        {
            using var doc = PdfDocument.Open(pdfBytes);
            for (var pageNum = 1; pageNum <= doc.NumberOfPages; pageNum++)
            {
                var page = doc.GetPage(pageNum);
                var imgs = page.GetImages()
                    .OrderByDescending(img => img.Bounds.Top)
                    .ThenBy(img => img.Bounds.Left)
                    .ToList();
                var pagePaths = new List<string>();
                foreach (var img in imgs)
                {
                    byte[]? imgBytes = null;
                    if (img.TryGetPng(out var png)) imgBytes = png;
                    else if (img.TryGetBytesAsMemory(out var mem)) imgBytes = mem.ToArray();
                    else if (img.RawMemory.Length > 0) imgBytes = img.RawMemory.ToArray();
                    if (imgBytes is null || imgBytes.Length < 64) continue;

                    var ext = imgBytes[0] == 0xFF && imgBytes[1] == 0xD8 ? "jpg" : "png";
                    using var ms = new MemoryStream(imgBytes);
                    var path = await _fileStorage.SaveRawAsync(ms, $"img_{Guid.NewGuid()}.{ext}");
                    pagePaths.Add(path);
                }
                result.Add(pagePaths);
            }
            _logger.LogInformation("[PdfPig] {C} embedded resim, {P} sayfa",
                result.Sum(p => p.Count), result.Count);
        }
        catch (Exception ex) { _logger.LogWarning("[PdfPig] Hata: {M}", ex.Message); }
        return result;
    }

    private record PreparedDoc(byte[] Bytes, List<string> Paths);


    private async Task<PreparedDoc> PrepareXlsxWithPlaceholdersAsync(byte[] originalBytes)
    {
        var paths = new List<string>();
        try
        {
            using var inMs = new MemoryStream(originalBytes);
            using var workbook = new XLWorkbook(inMs);
            foreach (var ws in workbook.Worksheets)
            {
                var pictures = ws.Pictures
                    .OrderBy(p => p.TopLeftCell?.Address.RowNumber ?? 0)
                    .ThenBy(p => p.TopLeftCell?.Address.ColumnNumber ?? 0)
                    .ToList();
                foreach (var pic in pictures)
                {
                    using var imgMs = new MemoryStream();
                    pic.ImageStream.CopyTo(imgMs);
                    var imgBytes = imgMs.ToArray();
                    if (imgBytes.Length < 64) continue;

                    var ext = imgBytes[0] == 0xFF && imgBytes[1] == 0xD8 ? "jpg"
                            : imgBytes[0] == 0x89 ? "png"
                            : imgBytes[0] == 0x47 ? "gif"
                            : imgBytes[0] == 0x42 ? "bmp" : "png";
                    using var save = new MemoryStream(imgBytes);
                    var path = await _fileStorage.SaveRawAsync(save, $"img_{Guid.NewGuid()}.{ext}");
                    paths.Add(path);

                    var token = $"[[EMBED_IMG_{paths.Count - 1}]]";
                    var cell = pic.TopLeftCell;
                    if (cell is not null)
                    {
                        var existing = cell.GetString()?.Trim();
                        cell.Value = string.IsNullOrWhiteSpace(existing) ? token : $"{existing} {token}";
                    }
                    pic.Delete();
                }
            }
            using var outMs = new MemoryStream();
            workbook.SaveAs(outMs);
            _logger.LogInformation("[Xlsx] {C} embedded resim placeholder ile işaretlendi", paths.Count);
            return new PreparedDoc(outMs.ToArray(), paths);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("[Xlsx] Placeholder hatası: {M}", ex.Message);
            return new PreparedDoc(originalBytes, paths);
        }
    }


    private async Task<byte[]> ExtractHtmlFromMhtmlAsync(byte[] mhtmlBytes)
    {
        try
        {
            using var ms = new MemoryStream(mhtmlBytes);
            var msg = MimeKit.MimeMessage.Load(ms);

            string? html = null;
            var cidToDataUri = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var allImageDataUris = new List<string>(); // HTML'in referans etmediği "sahipsiz" resimleri buradan ekleyeceğiz

            foreach (var part in msg.BodyParts)
            {
                if (part is not MimeKit.MimePart mime) continue;
                if (mime.ContentType.MimeType == "text/html")
                {
                    using var sr = new StreamReader(mime.Content.Open());
                    html = sr.ReadToEnd();
                }
                else if (mime.ContentType.MediaType == "image")
                {
                    using var imgMs = new MemoryStream();
                    mime.Content.DecodeTo(imgMs);
                    var b64 = Convert.ToBase64String(imgMs.ToArray());
                    var dataUri = $"data:{mime.ContentType.MimeType};base64,{b64}";
                    allImageDataUris.Add(dataUri);

                    var cid = mime.ContentId?.Trim('<', '>');
                    if (!string.IsNullOrEmpty(cid)) cidToDataUri[cid] = dataUri;
                    var loc = mime.ContentLocation?.ToString();
                    if (!string.IsNullOrEmpty(loc)) cidToDataUri[loc] = dataUri;
                    if (!string.IsNullOrEmpty(mime.FileName)) cidToDataUri[mime.FileName] = dataUri;
                }
            }

            if (string.IsNullOrWhiteSpace(html))
            {
                _logger.LogWarning("[MHTML] HTML body bulunamadı, raw bytes geri döndürülüyor");
                return mhtmlBytes;
            }

            // 1) cid: ve içerik-konum referanslarını data URI ile değiştir
            foreach (var (key, dataUri) in cidToDataUri)
            {
                html = html.Replace($"cid:{key}", dataUri, StringComparison.OrdinalIgnoreCase);
                html = html.Replace($"\"{key}\"", $"\"{dataUri}\"", StringComparison.OrdinalIgnoreCase);
                html = html.Replace($"'{key}'", $"'{dataUri}'", StringComparison.OrdinalIgnoreCase);
            }

            // 2) HTML'i parse et: dış URL'li <img> tag'lerini fetch edip data URI'ye çevir
            var doc = new HtmlAgilityPack.HtmlDocument();
            doc.LoadHtml(html);
            var imgNodes = doc.DocumentNode.SelectNodes("//img");
            var referencedDataUris = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int fetched = 0, alreadyData = 0;
            if (imgNodes != null)
            {
                // IHttpClientFactory'den havuzlu client — DNS güncellemeleri ve socket exhaustion'a güvenli.
                var http = _httpFactory.CreateClient();
                http.Timeout = TimeSpan.FromSeconds(10);
                foreach (var img in imgNodes)
                {
                    var src = img.GetAttributeValue("src", string.Empty);
                    if (string.IsNullOrWhiteSpace(src)) continue;
                    if (src.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                    {
                        referencedDataUris.Add(src);
                        alreadyData++;
                        continue;
                    }
                    if (src.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                        src.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            var bytes = await http.GetByteArrayAsync(src);
                            if (bytes.Length < 64) continue;
                            var mime = bytes[0] == 0xFF && bytes[1] == 0xD8 ? "image/jpeg"
                                     : bytes[0] == 0x89 ? "image/png"
                                     : bytes[0] == 0x47 ? "image/gif"
                                     : bytes[0] == 0x42 ? "image/bmp" : "image/png";
                            var dataUri = $"data:{mime};base64,{Convert.ToBase64String(bytes)}";
                            img.SetAttributeValue("src", dataUri);
                            referencedDataUris.Add(dataUri);
                            fetched++;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogDebug("[MHTML] Dış URL fetch hatası: {Url} → {Msg}", src, ex.Message);
                        }
                    }
                }
                html = doc.DocumentNode.OuterHtml;
            }

            // 3) MHTML embed'leri arasında HTML hiç referans etmeyenleri (sahipsiz resim) body sonuna ekle
            var unreferencedImages = allImageDataUris.Where(u => !referencedDataUris.Contains(u)
                                                              && !html.Contains(u)).ToList();
            if (unreferencedImages.Count > 0)
            {
                var sb = new StringBuilder();
                sb.AppendLine("<div>");
                foreach (var dataUri in unreferencedImages)
                    sb.AppendLine($"<p><img src=\"{dataUri}\" style=\"max-width:600px\" /></p>");
                sb.AppendLine("</div>");
                var idx = html.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
                html = idx >= 0
                    ? html.Insert(idx, sb.ToString())
                    : html + sb.ToString();
            }

            _logger.LogInformation("[MHTML] {Chars} kar, {Embed} embed, {Fetch} dış URL fetch, {Data} mevcut data URI, {Unref} sahipsiz",
                html.Length, cidToDataUri.Count, fetched, alreadyData, unreferencedImages.Count);
            return Encoding.UTF8.GetBytes(html);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("[MHTML] Parse hatası: {Msg} — raw bytes geri döndürülüyor", ex.Message);
            return mhtmlBytes;
        }
    }

    private static bool IsMhtml(byte[] bytes)
    {
        if (bytes.Length < 20) return false;
        var head = Encoding.ASCII.GetString(bytes, 0, Math.Min(200, bytes.Length));
        return head.StartsWith("Message-ID:", StringComparison.OrdinalIgnoreCase)
            || head.StartsWith("MIME-Version:", StringComparison.OrdinalIgnoreCase)
            || head.StartsWith("From:", StringComparison.OrdinalIgnoreCase)
            || head.Contains("Content-Type: multipart/related", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<byte[]> ReadAllBytesAsync(Stream stream)
    {
        if (stream.CanSeek) stream.Position = 0;
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms);
        if (stream.CanSeek) stream.Position = 0;
        return ms.ToArray();
    }

    private static string MimeFor(FileType ft) => ft switch
    {
        FileType.Pdf  => "application/pdf",
        FileType.Docx => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        FileType.Xlsx => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        FileType.Csv  => "text/csv",
        FileType.Doc  => "application/msword",
        _             => "application/octet-stream"
    };
}
