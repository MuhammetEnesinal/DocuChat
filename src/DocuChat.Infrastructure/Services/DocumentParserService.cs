using ClosedXML.Excel;
using CsvHelper;
using DocuChat.Application.Abstractions;
using DocuChat.Domain.Enums;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using PDFtoImage;
using SkiaSharp;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Net.Http.Json;
using System.Text.Json;
using Tesseract;

namespace DocuChat.Infrastructure.Services;

public class DocumentParserService : IDocumentParser
{
    private readonly int _chunkSize;
    private readonly int _overlap;
    private readonly string _tessDataPath;
    private readonly string _tessLang;
    private readonly string? _groqApiKey;
    private readonly string _groqVisionModel;

    private const int RenderDpi = 400;
    private const float ColGapRatio = 0.03f; // Sayfa genişliğinin %3'ünden büyük boşluk → sütun sınırı
    private const int MinTableCols = 3;
    private const int MinTableRows = 2;

    // ── Encoding fix ──────────────────────────────────────────────────────
    private static readonly Regex EngContractions =
        new(@"n't|'s\b|'re\b|'ll\b|'ve\b|'m\b|'d\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex TurkishSuffix =
        new(@"'(nın|nin|nun|nün|na|ne|nda|nde|ndan|nden|daki|deki|nu|nü|da|de|ta|te|dan|den|ya|ye|yı|yi|yu|yü|ın|in|un|ün|ı|i|u|ü|a|e)\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex AbbrevSuffix =
        new(@"(?<=[A-ZĞÜŞİÖÇ])' (?=[a-zA-ZğüşıöçĞÜŞİÖÇ])", RegexOptions.Compiled);

    private const string L = @"a-zA-ZğüşıöçĞÜŞİÖÇ";
    private static readonly Regex AposInWord = new(@"(?<=[" + L + @"])'(?=[" + L + @"])", RegexOptions.Compiled);
    private static readonly Regex AposWordStart = new(@"(?<=[^" + L + @"])'(?=[" + L + @"])", RegexOptions.Compiled);
    private static readonly Regex AposWordEnd = new(@"(?<=[" + L + @"])'(?=[^" + L + @"])", RegexOptions.Compiled);
    private static readonly Regex AposLineStart = new(@"^'(?=[" + L + @"])", RegexOptions.Compiled | RegexOptions.Multiline);

    private static readonly Dictionary<string, string> Ligatures = new()
    {
        ["\uFB01"] = "fi",
        ["\uFB02"] = "fl",
        ["\uFB00"] = "ff",
        ["\uFB03"] = "ffi",
        ["\uFB04"] = "ffl",
    };

    public DocumentParserService(Microsoft.Extensions.Configuration.IConfiguration cfg)
    {
        _chunkSize = int.Parse(cfg["Chunking:ChunkSize"] ?? "800");
        _overlap = int.Parse(cfg["Chunking:Overlap"] ?? "150");
        _tessDataPath = cfg["Tesseract:DataPath"] ?? @"C:\Users\bsstajyer\AppData\Local\Programs\Tesseract-OCR\tessdata";
        _tessLang = cfg["Tesseract:Language"] ?? "tur+eng";
        _groqApiKey = cfg["Llm:ApiKey"];
        _groqVisionModel = cfg["GroqVision:Model"] ?? "meta-llama/llama-4-scout-17b-16e-instruct";
    }

    public IEnumerable<string> Parse(Stream stream, FileType fileType)
    {
        // Her format için extract + semantic chunk
        // XLSX ve CSV: tablo marker'larıyla wrap edilmiş metin döner → Chunk() bölmez
        // PDF: Groq Vision sayfa bazlı, tablo marker'lı → Chunk() bölmez
        // DOCX: başlık/tablo ayrımı yapılmış → Chunk() akıllıca böler
        // TXT: düz metin → Chunk() cümle bazlı böler

        var text = fileType switch
        {
            FileType.Pdf => ExtractPdf(stream),
            FileType.Docx => ExtractDocx(stream),
            FileType.Xlsx => ExtractXlsx(stream),
            FileType.Csv => ExtractCsv(stream),
            _ => ExtractTxt(stream),
        };

        var cleaned = CleanText(text);
        return Chunk(cleaned);
    }

    // ── PDF ───────────────────────────────────────────────────────────────
    private string ExtractPdf(Stream stream)
    {
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        ms.Position = 0;

        var sb = new StringBuilder();

        var useGroqVision = !string.IsNullOrWhiteSpace(_groqApiKey);
        Console.WriteLine(useGroqVision ? "[OCR] Motor: Groq Vision" : "[OCR] Motor: Tesseract");

        if (useGroqVision)
        {
            try
            {
                var pageImages = Conversion.ToImages(ms, options: new RenderOptions { Dpi = RenderDpi });
                foreach (var bitmap in pageImages)
                {
                    try
                    {
                        var pageText = OcrPageWithGroqAsync(bitmap, _groqApiKey!, _groqVisionModel).GetAwaiter().GetResult();
                        if (!string.IsNullOrWhiteSpace(pageText)) sb.AppendLine(pageText);
                    }
                    finally { bitmap.Dispose(); }
                }
                return sb.ToString();
            }
            catch (Exception ex)
            {
                Console.WriteLine("[OCR] Groq Vision hata, Tesseract'a fallback: " + ex.Message);
                sb.Clear(); ms.Position = 0;
            }
        }

        TesseractEngine? engine = null;
        try { engine = new TesseractEngine(_tessDataPath, _tessLang, EngineMode.LstmOnly); }
        catch { }

        try
        {
            ms.Position = 0;
            var pageImages = Conversion.ToImages(ms, options: new RenderOptions { Dpi = RenderDpi });
            foreach (var bitmap in pageImages)
            {
                try
                {
                    var pageText = engine is not null ? OcrPageWithTesseract(bitmap, engine) : string.Empty;
                    if (!string.IsNullOrWhiteSpace(pageText)) sb.AppendLine(pageText);
                }
                finally { bitmap.Dispose(); }
            }
        }
        catch (Exception ex) { sb.AppendLine($"[PDF okuma hatası: {ex.Message}]"); }
        finally { engine?.Dispose(); }
        return sb.ToString();
    }

    private static async Task<string> OcrPageWithGroqAsync(SKBitmap bitmap, string apiKey, string model)
    {
        try
        {
            using var image = SKImage.FromBitmap(bitmap);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            var base64 = Convert.ToBase64String(data.ToArray());

            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
            http.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");

            var payload = new
            {
                model,
                max_tokens = 4096,
                temperature = 0,
                messages = new object[]
                {
                    new
                    {
                        role = "user",
                        content = new object[]
                        {
                            new { type = "text", text = "Bu görseldeki tüm metni olduğu gibi çıkar. Sadece metni ver, başka hiçbir şey ekleme." },
                            new { type = "image_url", image_url = new { url = $"data:image/png;base64,{base64}" } }
                        }
                    }
                }
            };

            var response = await http.PostAsJsonAsync("https://api.groq.com/openai/v1/chat/completions", payload);
            var json = await response.Content.ReadFromJsonAsync<JsonElement>();
            var text = json.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString()?.Trim() ?? string.Empty;
            Console.WriteLine($"[OCR] Groq Vision sayfa uzunluk: {text.Length}");
            return text;
        }
        catch (Exception ex) { Console.WriteLine("[OCR] Groq Vision hata: " + ex.Message); return string.Empty; }
    }

    private static string OcrPageWithTesseract(SKBitmap bitmap, TesseractEngine engine)
    {
        try
        {
            using var image = SKImage.FromBitmap(bitmap);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            using var pix = Pix.LoadFromMemory(data.ToArray());
            using var ocrPage = engine.Process(pix, PageSegMode.Auto);
            var words = ParseTsv(ocrPage.GetTsvText(1), bitmap.Width);
            if (!words.Any()) return ocrPage.GetText()?.Trim() ?? string.Empty;
            var rows = GroupIntoRows(words, bitmap.Height);
            var sb = new StringBuilder();
            foreach (var row in rows)
                sb.AppendLine(string.Join(" ", row.Select(w => w.Text)));
            return sb.ToString().Trim();
        }
        catch { return string.Empty; }
    }

    // ── TSV parse ─────────────────────────────────────────────────────────
    private static List<TsvWord> ParseTsv(string tsv, int pageWidth)
    {
        var words = new List<TsvWord>();
        if (string.IsNullOrWhiteSpace(tsv)) return words;

        foreach (var line in tsv.Split('\n'))
        {
            var cols = line.Split('\t');
            if (cols.Length < 12) continue;
            if (!int.TryParse(cols[0], out var level) || level != 5) continue;
            if (!float.TryParse(cols[6], NumberStyles.Float, CultureInfo.InvariantCulture, out var left)) continue;
            if (!float.TryParse(cols[7], NumberStyles.Float, CultureInfo.InvariantCulture, out var top)) continue;
            if (!float.TryParse(cols[8], NumberStyles.Float, CultureInfo.InvariantCulture, out var width)) continue;
            if (!float.TryParse(cols[9], NumberStyles.Float, CultureInfo.InvariantCulture, out var height)) continue;
            if (!float.TryParse(cols[10], NumberStyles.Float, CultureInfo.InvariantCulture, out var conf)) continue;
            if (conf < 0) continue;

            var text = string.Join("\t", cols.Skip(11)).Trim();
            if (string.IsNullOrWhiteSpace(text)) continue;

            words.Add(new TsvWord(text, left, top, left + width, top + height, pageWidth));
        }

        return words;
    }

    // ── Kelimeleri satırlara grupla ───────────────────────────────────────
    private static List<List<TsvWord>> GroupIntoRows(List<TsvWord> words, int pageHeight)
    {
        // Satır toleransı: ortalama harf yüksekliğinin yarısı
        var avgH = words.Average(w => w.Bottom - w.Top);
        var rowTol = avgH * 0.6f;

        var sorted = words.OrderBy(w => w.Top).ThenBy(w => w.Left).ToList();
        var rows = new List<List<TsvWord>>();
        var current = new List<TsvWord> { sorted[0] };
        var refY = sorted[0].CenterY;

        for (var i = 1; i < sorted.Count; i++)
        {
            var w = sorted[i];
            if (Math.Abs(w.CenterY - refY) <= rowTol)
            {
                current.Add(w);
            }
            else
            {
                rows.Add(current.OrderBy(x => x.Left).ToList());
                current = new List<TsvWord> { w };
                refY = w.CenterY;
            }
        }

        if (current.Any())
            rows.Add(current.OrderBy(x => x.Left).ToList());

        return rows;
    }

    // ── Sayfa metnini oluştur: tablo/metin ayrımı ─────────────────────────
    private static string BuildPageText(List<List<TsvWord>> rows, int pageWidth)
    {
        var result = new StringBuilder();
        var tableCandidate = new List<List<TsvWord>>();

        void FlushTable()
        {
            if (tableCandidate.Count < MinTableRows)
            {
                foreach (var r in tableCandidate)
                    result.AppendLine(RowToText(r));
            }
            else
            {
                // Her satırın sütun sayısını kontrol et
                var colCounts = tableCandidate.Select(r => SplitRowIntoCols(r, pageWidth).Count).ToList();
                var maxCols = colCounts.Max();

                if (maxCols >= MinTableCols)
                    result.AppendLine(FormatTable(tableCandidate, pageWidth));
                else
                    foreach (var r in tableCandidate)
                        result.AppendLine(RowToText(r));
            }
            tableCandidate.Clear();
        }

        foreach (var row in rows)
        {
            var cols = SplitRowIntoCols(row, pageWidth);

            if (cols.Count >= MinTableCols)
            {
                tableCandidate.Add(row);
            }
            else
            {
                if (tableCandidate.Any()) FlushTable();
                result.AppendLine(RowToText(row));
            }
        }

        if (tableCandidate.Any()) FlushTable();

        return result.ToString().Trim();
    }

    // Satırdaki kelimeleri aralarındaki boşluğa göre sütunlara böl
    private static List<List<TsvWord>> SplitRowIntoCols(List<TsvWord> row, int pageWidth)
    {
        if (!row.Any()) return new();

        var minGap = pageWidth * ColGapRatio; // sayfa genişliğinin %3'ü
        var cols = new List<List<TsvWord>>();
        var current = new List<TsvWord> { row[0] };

        for (var i = 1; i < row.Count; i++)
        {
            var gap = row[i].Left - row[i - 1].Right;
            if (gap > minGap)
            {
                cols.Add(current);
                current = new List<TsvWord>();
            }
            current.Add(row[i]);
        }

        if (current.Any()) cols.Add(current);
        return cols;
    }

    // Tablo satırlarını formatla
    private static string FormatTable(List<List<TsvWord>> tableRows, int pageWidth)
    {
        if (!tableRows.Any()) return string.Empty;

        // Tüm satırlardaki sütun X başlangıçlarını topla → unified col positions
        var allColStarts = tableRows
            .SelectMany(r => SplitRowIntoCols(r, pageWidth))
            .Select(col => col.Min(w => w.Left))
            .OrderBy(x => x)
            .ToList();

        // Yakın X pozisyonlarını birleştir (cluster)
        var colPositions = ClusterPositions(allColStarts, pageWidth * ColGapRatio);

        if (colPositions.Count < MinTableCols)
            return string.Join("\n", tableRows.Select(r => RowToText(r)));

        var sb = new StringBuilder();
        sb.AppendLine("[TABLO BAŞLANGIÇ]");

        var allCells = tableRows.Select(row => AssignToCols(row, colPositions, pageWidth)).ToList();
        var headers = allCells[0].Select(c => c.Trim()).ToList();

        sb.AppendLine("Başlıklar: " + string.Join(" | ", headers.Where(h => !string.IsNullOrWhiteSpace(h))));

        foreach (var cells in allCells.Skip(1))
        {
            var parts = new List<string>();
            for (var i = 0; i < cells.Count; i++)
            {
                var cell = cells[i].Trim();
                if (string.IsNullOrWhiteSpace(cell)) continue;
                var header = i < headers.Count && !string.IsNullOrWhiteSpace(headers[i])
                    ? headers[i] : $"Sütun{i + 1}";
                parts.Add($"{header}: {cell}");
            }
            if (parts.Any()) sb.AppendLine(string.Join(", ", parts));
        }

        sb.AppendLine("[TABLO BİTİŞ]");
        return sb.ToString();
    }

    // Yakın X pozisyonlarını grupla
    private static List<float> ClusterPositions(List<float> positions, float tolerance)
    {
        if (!positions.Any()) return new();

        var clusters = new List<float>();
        var current = positions[0];
        var count = 1;

        for (var i = 1; i < positions.Count; i++)
        {
            if (positions[i] - current / count <= tolerance)
            {
                current += positions[i];
                count++;
            }
            else
            {
                clusters.Add(current / count);
                current = positions[i];
                count = 1;
            }
        }

        clusters.Add(current / count);
        return clusters;
    }

    // Satırdaki kelimeleri sütun pozisyonlarına göre hücrelere ata
    private static List<string> AssignToCols(List<TsvWord> row, List<float> colPositions, int pageWidth)
    {
        var cells = new string[colPositions.Count];
        for (var i = 0; i < cells.Length; i++) cells[i] = string.Empty;

        var rowCols = SplitRowIntoCols(row, pageWidth);
        foreach (var col in rowCols)
        {
            var colLeft = col.Min(w => w.Left);
            var bestIdx = 0;
            var bestDist = float.MaxValue;

            for (var i = 0; i < colPositions.Count; i++)
            {
                var dist = Math.Abs(colLeft - colPositions[i]);
                if (dist < bestDist) { bestDist = dist; bestIdx = i; }
            }

            var colText = string.Join(" ", col.Select(w => w.Text));
            cells[bestIdx] += string.IsNullOrEmpty(cells[bestIdx]) ? colText : " " + colText;
        }

        return cells.ToList();
    }

    private static string RowToText(List<TsvWord> row)
        => string.Join(" ", row.Select(w => w.Text));

    // ── DOCX ─────────────────────────────────────────────────────────────
    private static string ExtractDocx(Stream stream)
    {
        var sb = new StringBuilder();
        try
        {
            using var doc = WordprocessingDocument.Open(stream, false);
            var body = doc.MainDocumentPart?.Document?.Body;
            if (body is null) return string.Empty;

            foreach (var element in body.ChildElements)
            {
                switch (element)
                {
                    case DocumentFormat.OpenXml.Wordprocessing.Paragraph para:
                        var paraText = BuildParagraphText(para);
                        if (!string.IsNullOrWhiteSpace(paraText))
                            sb.AppendLine(paraText);
                        break;
                    case DocumentFormat.OpenXml.Wordprocessing.Table table:
                        sb.AppendLine(ExtractDocxTable(table));
                        break;
                }
            }
        }
        catch { }
        return sb.ToString();
    }

    private static string BuildParagraphText(DocumentFormat.OpenXml.Wordprocessing.Paragraph para)
    {
        var text = para.InnerText.Trim();
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        var styleId = para.ParagraphProperties?.ParagraphStyleId?.Val?.Value ?? string.Empty;
        if (styleId.StartsWith("Heading", StringComparison.OrdinalIgnoreCase) ||
            styleId.StartsWith("Başlık", StringComparison.OrdinalIgnoreCase))
            return $"\n## {text}";

        if (para.ParagraphProperties?.NumberingProperties is not null)
            return $"• {text}";

        return text;
    }

    private static string ExtractDocxTable(DocumentFormat.OpenXml.Wordprocessing.Table table)
    {
        var rows = table.Elements<TableRow>().ToList();
        if (!rows.Any()) return string.Empty;

        var sb = new StringBuilder();
        sb.AppendLine("[TABLO BAŞLANGIÇ]");

        var headers = rows[0].Elements<TableCell>().Select(c => c.InnerText.Trim()).ToList();
        sb.AppendLine("Başlıklar: " + string.Join(" | ", headers.Where(h => !string.IsNullOrWhiteSpace(h))));

        foreach (var row in rows.Skip(1))
        {
            var cells = row.Elements<TableCell>().Select(c => c.InnerText.Trim()).ToList();
            var parts = new List<string>();
            for (var i = 0; i < cells.Count; i++)
            {
                if (string.IsNullOrWhiteSpace(cells[i])) continue;
                var header = i < headers.Count && !string.IsNullOrWhiteSpace(headers[i])
                    ? headers[i] : $"Sütun{i + 1}";
                parts.Add($"{header}: {cells[i]}");
            }
            if (parts.Any()) sb.AppendLine(string.Join(", ", parts));
        }

        sb.AppendLine("[TABLO BİTİŞ]");
        return sb.ToString();
    }

    // ── XLSX ─────────────────────────────────────────────────────────────
    private static string ExtractXlsx(Stream stream)
    {
        var sb = new StringBuilder();
        try
        {
            using var wb = new XLWorkbook(stream);
            foreach (var ws in wb.Worksheets)
            {
                var allRows = ws.RowsUsed().Where(r => !r.IsHidden).ToList();
                if (!allRows.Any()) continue;

                // Başlık satırını bul: birden fazla dolu sütunu olan ilk satır
                var headerRowIdx = 0;
                for (var ri = 0; ri < Math.Min(5, allRows.Count); ri++)
                {
                    if (allRows[ri].CellsUsed().Count() >= 2) { headerRowIdx = ri; break; }
                }

                var headers = allRows[headerRowIdx].CellsUsed()
                    .OrderBy(c => c.Address.ColumnNumber)
                    .Select(c => c.Value.ToString()?.Trim() ?? string.Empty).ToList();

                // [TABLO BAŞLANGIÇ] marker ile wrap et
                // Chunk() metodu bu bloğu boyuta bakmaksızın bölmeden tek parça tutar
                sb.AppendLine("[TABLO BAŞLANGIÇ]");
                sb.AppendLine($"Sayfa: {ws.Name}");
                sb.AppendLine("Başlıklar: " + string.Join(" | ", headers.Where(h => !string.IsNullOrWhiteSpace(h))));

                foreach (var row in allRows.Skip(headerRowIdx + 1))
                {
                    var cells = row.CellsUsed()
                        .OrderBy(c => c.Address.ColumnNumber)
                        .Select(c => c.Value.ToString()?.Trim() ?? string.Empty).ToList();
                    var parts = new List<string>();
                    for (var i = 0; i < cells.Count; i++)
                    {
                        if (string.IsNullOrWhiteSpace(cells[i])) continue;
                        var header = i < headers.Count && !string.IsNullOrWhiteSpace(headers[i])
                            ? headers[i] : $"Sütun{i + 1}";
                        parts.Add($"{header}: {cells[i]}");
                    }
                    if (parts.Any()) sb.AppendLine(string.Join(", ", parts));
                }

                sb.AppendLine("[TABLO BİTİŞ]");
            }
        }
        catch { }
        return sb.ToString();
    }


    // ── CSV ──────────────────────────────────────────────────────────────
    private static string ExtractCsv(Stream stream)
    {
        var sb = new StringBuilder();
        try
        {
            var enc = DetectEncoding(stream);
            stream.Position = 0;
            using var peekReader = new StreamReader(new MemoryStream(ReadBytes(stream)), enc);
            var firstLine = peekReader.ReadLine() ?? string.Empty;
            var delimiter = firstLine.Count(c => c == ';') > firstLine.Count(c => c == ',') ? ';' : ',';

            stream.Position = 0;
            var csvConfig = new CsvHelper.Configuration.CsvConfiguration(CultureInfo.InvariantCulture)
            {
                Delimiter = delimiter.ToString(),
                BadDataFound = null,
                MissingFieldFound = null,
            };

            using var reader = new StreamReader(stream, enc);
            using var csv = new CsvReader(reader, csvConfig);
            var headers = new List<string>();
            var isFirst = true;

            while (csv.Read())
            {
                if (isFirst)
                {
                    headers = Enumerable.Range(0, csv.Parser.Count)
                        .Select(i => csv.GetField(i) ?? string.Empty).ToList();
                    sb.AppendLine("Başlıklar: " + string.Join(" | ", headers));
                    isFirst = false;
                    continue;
                }

                var parts = new List<string>();
                for (var i = 0; i < csv.Parser.Count; i++)
                {
                    var val = csv.GetField(i) ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(val)) continue;
                    var header = i < headers.Count ? headers[i] : $"Sütun{i + 1}";
                    parts.Add($"{header}: {val}");
                }
                if (parts.Any()) sb.AppendLine(string.Join(", ", parts));
            }
        }
        catch { }
        var csvContent = sb.ToString().Trim();
        if (string.IsNullOrWhiteSpace(csvContent)) return string.Empty;
        return $"[TABLO BAŞLANGIÇ]\n{csvContent}\n[TABLO BİTİŞ]";
    }

    // ── TXT ──────────────────────────────────────────────────────────────
    private static string ExtractTxt(Stream stream)
    {
        try
        {
            var enc = DetectEncoding(stream);
            stream.Position = 0;
            using var reader = new StreamReader(stream, enc);
            return reader.ReadToEnd();
        }
        catch { return string.Empty; }
    }

    // ── Encoding tespiti ──────────────────────────────────────────────────
    private static Encoding DetectEncoding(Stream stream)
    {
        stream.Position = 0;
        var bom = new byte[4];
        var read = 0;
        int b;
        while (read < 4 && (b = stream.ReadByte()) != -1)
            bom[read++] = (byte)b;

        if (read >= 3 && bom[0] == 0xEF && bom[1] == 0xBB && bom[2] == 0xBF) return new UTF8Encoding(true);
        if (read >= 2 && bom[0] == 0xFF && bom[1] == 0xFE) return Encoding.Unicode;
        if (read >= 2 && bom[0] == 0xFE && bom[1] == 0xFF) return Encoding.BigEndianUnicode;

        stream.Position = 0;
        var sample = new byte[Math.Min(stream.Length, 8192)];
        var totalRead = 0;
        int bytesRead;
        while (totalRead < sample.Length &&
               (bytesRead = stream.Read(sample, totalRead, sample.Length - totalRead)) > 0)
            totalRead += bytesRead;

        try
        {
            var decoded = Encoding.UTF8.GetString(sample, 0, totalRead);
            if (!decoded.Contains('\uFFFD')) return Encoding.UTF8;
        }
        catch { }

        return Encoding.GetEncoding(1254);
    }

    private static byte[] ReadBytes(Stream stream)
    {
        stream.Position = 0;
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    // ── Temizleme ─────────────────────────────────────────────────────────
    private static string CleanText(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        text = text.Normalize(NormalizationForm.FormC);

        foreach (var (lig, rep) in Ligatures)
            text = text.Replace(lig, rep);

        text = Regex.Replace(text, @"[\u00A0\u00AD\u200B\u200C\u200D\uFEFF]", " ");
        text = text.Replace('\u2018', '\'').Replace('\u2019', '\'');
        text = text.Replace('\u201C', '"').Replace('\u201D', '"');
        text = text.Replace('`', '\'');

        var ph = new Dictionary<string, string>();
        var idx = 0;
        string Save(Match m) { var k = $"\x01{idx++}\x01"; ph[k] = m.Value; return k; }
        text = EngContractions.Replace(text, Save);
        text = TurkishSuffix.Replace(text, Save);
        text = AbbrevSuffix.Replace(text, Save);
        text = AposInWord.Replace(text, "i");
        text = AposWordStart.Replace(text, "i");
        text = AposWordEnd.Replace(text, "i");
        text = AposLineStart.Replace(text, "i");
        foreach (var (k, v) in ph) text = text.Replace(k, v);

        text = Regex.Replace(text, @"[│├└─┌┐┘┤┬┴┼╔╗╚╝╠╣╦╩╬►◄▲▼]", " ");
        text = Regex.Replace(text, @"[ \t]{2,}", " ");
        text = Regex.Replace(text, @"(\r?\n){3,}", "\n\n");
        text = string.Join("\n", text.Split('\n').Select(l => l.Trim()));
        text = text.Replace("\r", string.Empty);
        text = MergeWrappedLines(text);

        return text.Trim();
    }

    private static string MergeWrappedLines(string text)
    {
        var lines = text.Split('\n');
        var result = new List<string>();

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (string.IsNullOrWhiteSpace(line)) { result.Add(line); continue; }

            while (i + 1 < lines.Length)
            {
                var next = lines[i + 1].Trim();
                if (string.IsNullOrWhiteSpace(next)) break;
                var lastChar = line[^1];
                var firstChar = next[0];
                if (lastChar is '.' or '!' or '?' or ';' or ':') break;
                if (char.IsUpper(firstChar)) break;
                if (!char.IsLetter(firstChar)) break;
                line = line + " " + next;
                i++;
            }
            result.Add(line);
        }
        return string.Join("\n", result);
    }

    // ── Chunk'lama ────────────────────────────────────────────────────────
    // Tablo blokları ([TABLO BAŞLANGIÇ]...[TABLO BİTİŞ]) bölünmeden tek chunk olarak çıkar
    private IEnumerable<string> Chunk(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) yield break;

        // Önce tablo bloklarını koru: metni tablo/metin segmentlerine ayır
        var segments = SplitPreservingTables(text);

        var buffer = new StringBuilder();
        foreach (var segment in segments)
        {
            var isTable = segment.StartsWith("[TABLO BAŞLANGIÇ]");

            if (isTable)
            {
                // Tablo bloğunu önce flush et, sonra tek parça olarak ver
                if (buffer.Length > 0)
                {
                    var pending = buffer.ToString().Trim();
                    if (!string.IsNullOrWhiteSpace(pending)) yield return pending;
                    buffer.Clear();
                }
                // Tablo çok büyükse de bölme — tek chunk olarak ver
                yield return segment.Trim();
                continue;
            }

            // Normal metin: cümle bazlı chunk'la
            foreach (var sentence in SplitIntoSentences(segment))
            {
                if (buffer.Length > 0 && buffer.Length + sentence.Length > _chunkSize)
                {
                    var chunk = buffer.ToString().Trim();
                    if (!string.IsNullOrWhiteSpace(chunk)) yield return chunk;

                    var tail = chunk.Length > _overlap ? chunk[^_overlap..] : chunk;
                    buffer.Clear();
                    if (!string.IsNullOrWhiteSpace(tail)) buffer.Append(tail).Append(' ');
                }
                buffer.Append(sentence).Append(' ');
            }
        }

        var last = buffer.ToString().Trim();
        if (!string.IsNullOrWhiteSpace(last)) yield return last;
    }

    // Metni tablo blokları ve normal metin segmentlerine ayır
    private static IEnumerable<string> SplitPreservingTables(string text)
    {
        var start = 0;
        while (start < text.Length)
        {
            var tableStart = text.IndexOf("[TABLO BAŞLANGIÇ]", start, StringComparison.Ordinal);
            if (tableStart < 0)
            {
                // Geri kalan normal metin
                var remaining = text[start..];
                if (!string.IsNullOrWhiteSpace(remaining))
                    yield return remaining;
                yield break;
            }

            // Tablo öncesi normal metin
            if (tableStart > start)
            {
                var before = text[start..tableStart];
                if (!string.IsNullOrWhiteSpace(before))
                    yield return before;
            }

            // Tablo bloğu
            var tableEnd = text.IndexOf("[TABLO BİTİŞ]", tableStart, StringComparison.Ordinal);
            if (tableEnd < 0)
            {
                // Kapanmayan tablo — geri kalanı normal metin olarak ver
                var rest = text[tableStart..];
                if (!string.IsNullOrWhiteSpace(rest))
                    yield return rest;
                yield break;
            }

            tableEnd += "[TABLO BİTİŞ]".Length;
            yield return text[tableStart..tableEnd];
            start = tableEnd;
        }
    }

    private static IEnumerable<string> SplitIntoSentences(string text)
    {
        foreach (var part in Regex.Split(text, @"(?<=[.!?;])\s+|(?<=\n)\s*\n|\n"))
        {
            var s = part.Trim();
            if (!string.IsNullOrWhiteSpace(s)) yield return s;
        }
    }

    // ── Yardımcı record ───────────────────────────────────────────────────
    private record TsvWord(string Text, float Left, float Top, float Right, float Bottom, int PageWidth)
    {
        public float CenterY => (Top + Bottom) / 2f;
    }
}