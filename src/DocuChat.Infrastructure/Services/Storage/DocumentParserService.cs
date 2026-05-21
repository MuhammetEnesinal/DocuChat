using System.Diagnostics;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using DocuChat.Application.Interfaces.Services;
using DocuChat.Domain.Enums;
using DocumentFormat.OpenXml.Packaging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using UglyToad.PdfPig;
using A = DocumentFormat.OpenXml.Drawing;

namespace DocuChat.Infrastructure.Services.Storage;

/// <summary>
/// Mistral OCR text + dijital embedded resim extractor'ları (PdfPig/OpenXml/ClosedXML).
/// Embedded resimler markdown tablosundaki Resim/Görsel kolonunun boş hücrelerine sırayla yerleşir.
/// </summary>
public class DocumentParserService : IDocumentParser
{
    private static readonly string[] ImageColumnKeywords =
        { "resim", "görsel", "gorsel", "image", "foto", "photo" };

    private readonly IHttpClientFactory _httpFactory;
    private readonly IFileStorage _fileStorage;
    private readonly ILogger<DocumentParserService> _logger;
    private readonly string _mistralModel;
    private readonly int _chunkSize;
    private readonly string _sofficePath;

    public DocumentParserService(
        IConfiguration cfg,
        IFileStorage fileStorage,
        IHttpClientFactory httpFactory,
        ILogger<DocumentParserService> logger)
    {
        _httpFactory = httpFactory;
        _fileStorage = fileStorage;
        _logger = logger;
        _mistralModel = cfg["Mistral:Model"] ?? "mistral-ocr-latest";
        _chunkSize = int.TryParse(cfg["Chunking:ChunkSize"], out var cs) ? cs : 1200;
        _sofficePath = cfg["LibreOffice:Path"] ?? "soffice";
    }

    public async Task<IEnumerable<ParsedChunk>> ParseAsync(Stream stream, FileType fileType)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        var bytes = await ReadAllBytesAsync(stream);

        // DOCX / DOC: LibreOffice ile PDF'e çevir → PDF akışına yönlendir
        if (fileType is FileType.Docx or FileType.Doc)
        {
            var ext = fileType == FileType.Docx ? "docx" : "doc";
            // Confluence export'u gibi MHTML formatları .doc/.docx uzantısıyla gelebilir
            if (IsMhtml(bytes))
            {
                _logger.LogInformation("[Parser] MHTML formatı tespit edildi → MimeKit ile HTML'e çıkar");
                bytes = ExtractHtmlFromMhtml(bytes);
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
            var prep = PrepareXlsxWithPlaceholders(bytes);
            bytes = prep.Bytes;
            placeholderPaths = prep.Paths;
        }

        var mistralPages = await CallMistralAsync(bytes, mime);

        // PDF için sayfa-bazlı PdfPig (placeholder yöntemi PDF'te uygulanmaz)
        IReadOnlyList<List<string>> pdfEmbeddedPerPage = fileType == FileType.Pdf
            ? ExtractPdfEmbeddedPerPage(bytes)
            : Array.Empty<List<string>>();

        // Global image listesi: önce Mistral figure'ları (sayfa sırasıyla), sonra placeholder paths, sonra PDF embedded'lar
        var globalImages = new List<string>();
        foreach (var mp in mistralPages) globalImages.AddRange(mp.FigurePaths);
        var placeholderOffset = globalImages.Count;
        globalImages.AddRange(placeholderPaths);

        var allChunks = new List<ParsedChunk>();
        var mistralCumulative = 0;

        for (var i = 0; i < mistralPages.Count; i++)
        {
            var mp = mistralPages[i];
            var pageMd = mp.Markdown;

            // Mistral'ın lokal [IMG_REF:N] markerlarını GLOBAL index'e dönüştür
            for (var local = 0; local < mp.FigurePaths.Count; local++)
            {
                var g = mistralCumulative + local;
                pageMd = pageMd.Replace($"[IMG_REF:{local}]", $"__GLOBALMARKER__{g}__");
            }
            mistralCumulative += mp.FigurePaths.Count;

            // DOCX/XLSX placeholder'larını GLOBAL'e çevir
            pageMd = Regex.Replace(pageMd, @"\[\[EMBED_IMG_(\d+)\]\]", m =>
            {
                var n = int.Parse(m.Groups[1].Value);
                return $"__GLOBALMARKER__{placeholderOffset + n}__";
            });

            // PDF embedded'leri sayfa-bazlı tablo cell injection ile yerleştir
            if (fileType == FileType.Pdf && i < pdfEmbeddedPerPage.Count)
            {
                var embeddedForPage = pdfEmbeddedPerPage[i];
                if (embeddedForPage.Count > 0)
                {
                    var startingGlobal = globalImages.Count;
                    var (newMd, usedCount) = InjectIntoTableCells(pageMd, embeddedForPage, startingGlobal);
                    pageMd = newMd;
                    globalImages.AddRange(embeddedForPage.Take(usedCount));
                    if (usedCount < embeddedForPage.Count)
                        _logger.LogInformation("[Parser] PDF sayfa {N}: {Used}/{Total} embedded yerleşti",
                            i + 1, usedCount, embeddedForPage.Count);
                }
            }

            // Marker'ı son haline getir
            pageMd = Regex.Replace(pageMd, @"__GLOBALMARKER__(\d+)__", m => $"[IMG_REF:GLOBAL:{m.Groups[1].Value}]");
            allChunks.AddRange(ChunkPageMarkdown(pageMd));
        }

        // GLOBAL index'leri her chunk için lokal'e indirip ImagePath set et
        var final = new List<ParsedChunk>();
        foreach (var ch in allChunks)
        {
            var content = ch.Content;
            var chunkPaths = new List<string>();
            content = Regex.Replace(content, @"\[IMG_REF:GLOBAL:(\d+)\]", m =>
            {
                var g = int.Parse(m.Groups[1].Value);
                if (g < 0 || g >= globalImages.Count) return string.Empty;
                var path = globalImages[g];
                if (!chunkPaths.Contains(path)) chunkPaths.Add(path);
                return $"[IMG_REF:{chunkPaths.IndexOf(path)}]";
            });
            var imagePathJson = chunkPaths.Count > 0 ? JsonSerializer.Serialize(chunkPaths) : null;
            if (string.IsNullOrWhiteSpace(content)) continue;
            final.Add(new ParsedChunk(content.Trim(), imagePathJson, ch.Header));
        }

        _logger.LogInformation("[Parser] {FileType}: {Chunks} chunk, {Imgs} resim",
            fileType, final.Count, globalImages.Count);
        return final;
    }

    // ── LibreOffice DOCX/DOC → PDF ───────────────────────────────────────
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

    // ── Mistral OCR ──────────────────────────────────────────────────────
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

    // ── Embedded image extractors ────────────────────────────────────────
    private List<List<string>> ExtractPdfEmbeddedPerPage(byte[] pdfBytes)
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
                    var path = _fileStorage.SaveRawAsync(ms, $"img_{Guid.NewGuid()}.{ext}").GetAwaiter().GetResult();
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

    /// <summary>
    /// DOCX'i in-memory aç, her Drawing/Blip'i kaydet ve yerine [[EMBED_IMG_N]] metni koy.
    /// Mistral bu yazıyı markdown'a aktarır, biz de pozisyon kaybı olmadan IMG_REF'e dönüştürürüz.
    /// </summary>
    private PreparedDoc PrepareDocxWithPlaceholders(byte[] originalBytes)
    {
        var paths = new List<string>();
        var ms = new MemoryStream();
        ms.Write(originalBytes);
        ms.Position = 0;
        try
        {
            using (var word = WordprocessingDocument.Open(ms, true))
            {
                var main = word.MainDocumentPart;
                if (main?.Document?.Body is null)
                    return new PreparedDoc(originalBytes, paths);

                var drawings = main.Document.Body
                    .Descendants<DocumentFormat.OpenXml.Wordprocessing.Drawing>()
                    .ToList();
                foreach (var drawing in drawings)
                {
                    var blip = drawing.Descendants<A.Blip>().FirstOrDefault();
                    var rId = blip?.Embed?.Value;
                    if (string.IsNullOrEmpty(rId)) continue;
                    if (main.GetPartById(rId) is not ImagePart imgPart) continue;

                    using var imgMs = new MemoryStream();
                    using (var imgStream = imgPart.GetStream())
                        imgStream.CopyTo(imgMs);
                    var imgBytes = imgMs.ToArray();
                    if (imgBytes.Length < 64) continue;

                    var ext = imgPart.ContentType switch
                    {
                        "image/jpeg" => "jpg",
                        "image/png"  => "png",
                        "image/gif"  => "gif",
                        "image/bmp"  => "bmp",
                        _            => "png"
                    };
                    using var save = new MemoryStream(imgBytes);
                    var path = _fileStorage.SaveRawAsync(save, $"img_{Guid.NewGuid()}.{ext}").GetAwaiter().GetResult();
                    paths.Add(path);

                    var token = $" [[EMBED_IMG_{paths.Count - 1}]] ";
                    var textElem = new DocumentFormat.OpenXml.Wordprocessing.Text(token)
                    {
                        Space = DocumentFormat.OpenXml.SpaceProcessingModeValues.Preserve
                    };
                    // Drawing'in parent'ı bir Run — Drawing yerine Text koy
                    drawing.Parent?.ReplaceChild(textElem, drawing);
                }
                main.Document.Save();
            }
            _logger.LogInformation("[Docx] {C} embedded resim placeholder ile işaretlendi", paths.Count);
            return new PreparedDoc(ms.ToArray(), paths);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("[Docx] Placeholder hatası: {M}", ex.Message);
            return new PreparedDoc(originalBytes, paths);
        }
        finally
        {
            ms.Dispose();
        }
    }

    /// <summary>
    /// XLSX'i aç, her resmi kaydet, bulunduğu hücreye [[EMBED_IMG_N]] yaz ve resmi sil.
    /// </summary>
    private PreparedDoc PrepareXlsxWithPlaceholders(byte[] originalBytes)
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
                    var path = _fileStorage.SaveRawAsync(save, $"img_{Guid.NewGuid()}.{ext}").GetAwaiter().GetResult();
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

    // ── Tablo cell injection ─────────────────────────────────────────────
    /// <summary>
    /// Markdown'da Resim/Görsel/Image/Foto/Photo başlıklı kolonu olan tabloyu bul,
    /// data satırlarında boş olan resim hücrelerini sırayla [IMG_REF:GLOBAL:N] ile doldur.
    /// Yerleştirilen resim sayısını döner. Yerleşmeyenler düşürülür.
    /// </summary>
    private static (string Markdown, int UsedCount) InjectIntoTableCells(
        string md, IReadOnlyList<string> embeddedPaths, int startingGlobalIdx)
    {
        if (embeddedPaths.Count == 0) return (md, 0);

        var lines = md.Split('\n');
        var output = new StringBuilder();
        var queue = new Queue<int>(Enumerable.Range(startingGlobalIdx, embeddedPaths.Count));

        int? imageColIdx = null;
        bool sawSeparator = false;
        bool inTable = false;

        foreach (var line in lines)
        {
            var trimmed = line.TrimStart();
            var isTableLine = trimmed.StartsWith('|') && line.Contains('|');

            if (!isTableLine)
            {
                imageColIdx = null;
                sawSeparator = false;
                inTable = false;
                output.AppendLine(line);
                continue;
            }

            if (!inTable) inTable = true;
            var cells = SplitRow(line);

            // Separator (--- ---) satırı
            if (cells.All(c => { var t = c.Trim(); return t.Length > 0 && t.All(ch => ch == '-' || ch == ':'); }))
            {
                sawSeparator = true;
                output.AppendLine(line);
                continue;
            }

            // Resim kolonu tespiti (her tabloda en az bir kere yapılır)
            if (imageColIdx is null)
            {
                for (var c = 0; c < cells.Count; c++)
                {
                    var h = cells[c].Trim().ToLowerInvariant();
                    if (ImageColumnKeywords.Any(k => h == k || h.Contains(k))) { imageColIdx = c; break; }
                }
                output.AppendLine(line);
                continue;
            }

            // Data satırı — image hücresine sıradaki marker'ı koy
            if (sawSeparator && imageColIdx.Value < cells.Count && queue.Count > 0)
            {
                var cellText = cells[imageColIdx.Value].Trim();
                var nonImageCellsFilled = cells
                    .Where((_, idx) => idx != imageColIdx.Value)
                    .Count(c => !string.IsNullOrWhiteSpace(c));
                var isDataRow = nonImageCellsFilled >= 2;

                if (isDataRow && string.IsNullOrWhiteSpace(cellText))
                {
                    var g = queue.Dequeue();
                    cells[imageColIdx.Value] = $" __GLOBALMARKER__{g}__ ";
                    output.AppendLine(JoinRow(cells));
                    continue;
                }
            }

            output.AppendLine(line);
        }

        var used = embeddedPaths.Count - queue.Count;
        return (output.ToString(), used);
    }

    private static List<string> SplitRow(string line)
    {
        var t = line.Trim();
        if (t.StartsWith('|')) t = t[1..];
        if (t.EndsWith('|')) t = t[..^1];
        return t.Split('|').ToList();
    }

    private static string JoinRow(List<string> cells) => "|" + string.Join("|", cells) + "|";

    // ── Chunker — sayfa markdown'ını H1/H2 + max char ile böl ────────────
    private IEnumerable<ParsedChunk> ChunkPageMarkdown(string md)
    {
        if (string.IsNullOrWhiteSpace(md)) yield break;

        var sections = SplitByHeadings(md);
        foreach (var sec in sections)
        {
            foreach (var piece in SplitTooLong(sec.Content))
            {
                if (string.IsNullOrWhiteSpace(piece)) continue;
                yield return new ParsedChunk(piece.Trim(), null, sec.Header);
            }
        }
    }

    private record Section(string? Header, string Content);

    private static List<Section> SplitByHeadings(string md)
    {
        var lines = md.Split('\n');
        var sections = new List<Section>();
        string? currentHeader = null;
        var buf = new StringBuilder();

        foreach (var line in lines)
        {
            var t = line.TrimStart();
            if (t.StartsWith("# ") || t.StartsWith("## "))
            {
                if (buf.Length > 0)
                {
                    sections.Add(new Section(currentHeader, buf.ToString()));
                    buf.Clear();
                }
                currentHeader = t.TrimStart('#', ' ', '\t').Trim();
            }
            buf.AppendLine(line);
        }
        if (buf.Length > 0) sections.Add(new Section(currentHeader, buf.ToString()));
        return sections;
    }

    private IEnumerable<string> SplitTooLong(string piece)
    {
        if (piece.Length <= _chunkSize * 1.5) { yield return piece; yield break; }

        // Önce paragraf sınırlarından (\n\n), yetmezse satır sınırından (\n), o da yetmezse hard char split
        var paras = piece.Split(new[] { "\n\n" }, StringSplitOptions.None);
        var buf = new StringBuilder();

        void FlushBuf(List<string> sink)
        {
            if (buf.Length == 0) return;
            sink.Add(buf.ToString());
            buf.Clear();
        }

        var output = new List<string>();
        foreach (var p in paras)
        {
            if (buf.Length + p.Length + 2 > _chunkSize && buf.Length > 0) FlushBuf(output);

            // Paragraf tek başına bile çok uzunsa satır bazlı, sonra hard split
            if (p.Length > _chunkSize)
            {
                FlushBuf(output);
                foreach (var sub in HardSplit(p, _chunkSize)) output.Add(sub);
            }
            else
            {
                buf.AppendLine(p);
                buf.AppendLine();
            }
        }
        FlushBuf(output);

        foreach (var s in output) yield return s;
    }

    private static IEnumerable<string> HardSplit(string text, int maxLen)
    {
        // Önce satır sınırından dene
        var lines = text.Split('\n');
        var buf = new StringBuilder();
        foreach (var line in lines)
        {
            // Tek satır zaten çok uzunsa karakter bazlı kes
            if (line.Length > maxLen)
            {
                if (buf.Length > 0) { yield return buf.ToString(); buf.Clear(); }
                for (var i = 0; i < line.Length; i += maxLen)
                {
                    var len = Math.Min(maxLen, line.Length - i);
                    yield return line.Substring(i, len);
                }
                continue;
            }
            if (buf.Length + line.Length + 1 > maxLen && buf.Length > 0)
            {
                yield return buf.ToString();
                buf.Clear();
            }
            buf.AppendLine(line);
        }
        if (buf.Length > 0) yield return buf.ToString();
    }

    // ── MHTML → HTML extract ─────────────────────────────────────────────
    /// <summary>
    /// MHTML (multipart MIME) bytes'ından HTML body'sini çıkarır,
    /// inline image'ları base64 data URI olarak gömer ki LibreOffice render edebilsin.
    /// </summary>
    private byte[] ExtractHtmlFromMhtml(byte[] mhtmlBytes)
    {
        try
        {
            using var ms = new MemoryStream(mhtmlBytes);
            var msg = MimeKit.MimeMessage.Load(ms);

            string? html = null;
            var cidToDataUri = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var allImageDataUris = new List<string>(); // referans edilmemiş "orphan" resimleri buradan ekleyeceğiz

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
            //    + HTML'in referans ettiği MHTML embed'lerini tespit et
            var doc = new HtmlAgilityPack.HtmlDocument();
            doc.LoadHtml(html);
            var imgNodes = doc.DocumentNode.SelectNodes("//img");
            var referencedDataUris = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int fetched = 0, alreadyData = 0;
            if (imgNodes != null)
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
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
                            var bytes = http.GetByteArrayAsync(src).GetAwaiter().GetResult();
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

            // 3) MHTML embed'leri arasında HTML hiç referans etmeyenleri (orphan) body sonuna ekle
            var orphans = allImageDataUris.Where(u => !referencedDataUris.Contains(u)
                                                  && !html.Contains(u)).ToList();
            if (orphans.Count > 0)
            {
                var sb = new StringBuilder();
                sb.AppendLine("<div>");
                foreach (var dataUri in orphans)
                    sb.AppendLine($"<p><img src=\"{dataUri}\" style=\"max-width:600px\" /></p>");
                sb.AppendLine("</div>");
                var idx = html.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
                html = idx >= 0
                    ? html.Insert(idx, sb.ToString())
                    : html + sb.ToString();
            }

            _logger.LogInformation("[MHTML] {Chars} kar, {Embed} embed, {Fetch} dış URL fetch, {Data} mevcut data URI, {Orphan} orphan",
                html.Length, cidToDataUri.Count, fetched, alreadyData, orphans.Count);
            return Encoding.UTF8.GetBytes(html);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("[MHTML] Parse hatası: {Msg} — raw bytes geri döndürülüyor", ex.Message);
            return mhtmlBytes;
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────
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
