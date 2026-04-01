using ClosedXML.Excel;
using CsvHelper;
using DocuChat.Application.Abstractions;
using DocuChat.Domain.Enums;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Tesseract;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using PdfPage = UglyToad.PdfPig.Content.Page;

namespace DocuChat.Infrastructure.Services;

public class DocumentParserService : IDocumentParser
{
    private readonly int _chunkSize;
    private readonly int _overlap;
    private readonly string _tessDataPath;
    private readonly string _tessLang;

    private const int MinTextLength = 30;
    private const double RowTolerance = 3.0;
    private const double ColTolerance = 20.0;
    private const int MinColsForTable = 4;
    private const int MinTableRows = 3;
    private const double MinPageWidthRatio = 0.55;

    // ── Encoding fix pattern'ları ─────────────────────────────────────────
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
        _tessDataPath = cfg["Tesseract:DataPath"] ?? @"C:\Program Files\Tesseract-OCR\tessdata";
        _tessLang = cfg["Tesseract:Language"] ?? "tur+eng";
    }

    public IEnumerable<string> Parse(Stream stream, FileType fileType)
    {
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

        var sb = new StringBuilder();

        TesseractEngine? engine = null;
        try { engine = new TesseractEngine(_tessDataPath, _tessLang, EngineMode.Default); }
        catch { }

        try
        {
            ms.Position = 0;
            using var doc = PdfDocument.Open(ms);

            foreach (var page in doc.GetPages())
            {
                var pageText = page.Text?.Trim() ?? string.Empty;

                if (pageText.Length < MinTextLength && engine is not null)
                {
                    var ocrText = OcrPage(page, engine);
                    sb.AppendLine(string.IsNullOrWhiteSpace(ocrText) ? pageText : ocrText);
                }
                else
                {
                    // Tablo tespiti yap
                    var pageContent = ExtractPageWithTables(page);
                    sb.AppendLine(pageContent);
                }
            }
        }
        catch { }
        finally { engine?.Dispose(); }

        return sb.ToString();
    }

    // ── Sayfa içeriği: tablo tespiti ile ─────────────────────────────────
    private static string ExtractPageWithTables(PdfPage page)
    {
        var words = page.GetWords()
            .OrderByDescending(w => w.BoundingBox.Bottom)
            .ThenBy(w => w.BoundingBox.Left)
            .ToList();

        if (!words.Any()) return page.Text ?? string.Empty;

        var rows = GroupIntoRows(words, RowTolerance);
        var result = new StringBuilder();

        // Tablo adayı satırları topla
        var tableCandidate = new List<List<Word>>();
        var colPositions = new List<double>();

        void FlushTable()
        {
            if (tableCandidate.Count < MinTableRows)
            {
                // Yeterli satır yok → düz metin olarak yaz
                foreach (var r in tableCandidate)
                    result.AppendLine(string.Join(" ", r.Select(w => w.Text)));
            }
            else
            {
                // Sütunlar sayfanın en az %55'ine yayılıyor mu?
                var minX = tableCandidate.SelectMany(r => r).Min(w => w.BoundingBox.Left);
                var maxX = tableCandidate.SelectMany(r => r).Max(w => w.BoundingBox.Right);
                var span = (maxX - minX) / page.Width;

                if (span >= MinPageWidthRatio)
                    result.AppendLine(FormatTable(tableCandidate, colPositions));
                else
                    foreach (var r in tableCandidate)
                        result.AppendLine(string.Join(" ", r.Select(w => w.Text)));
            }
            tableCandidate.Clear();
            colPositions.Clear();
        }

        foreach (var row in rows)
        {
            if (row.Count >= MinColsForTable)
            {
                if (!tableCandidate.Any())
                    colPositions = row.Select(w => w.BoundingBox.Left).ToList();
                tableCandidate.Add(row);
            }
            else
            {
                if (tableCandidate.Any()) FlushTable();
                result.AppendLine(string.Join(" ", row.Select(w => w.Text)));
            }
        }

        if (tableCandidate.Any()) FlushTable();

        return result.ToString();
    }

    // Kelimeleri Y koordinatına göre satırlara grupla
    private static List<List<Word>> GroupIntoRows(List<Word> words, double tolerance)
    {
        var rows = new List<List<Word>>();
        var current = new List<Word> { words[0] };
        var refY = words[0].BoundingBox.Bottom;

        for (var i = 1; i < words.Count; i++)
        {
            var y = words[i].BoundingBox.Bottom;
            if (Math.Abs(y - refY) <= tolerance)
            {
                current.Add(words[i]);
            }
            else
            {
                rows.Add(current.OrderBy(w => w.BoundingBox.Left).ToList());
                current = new List<Word> { words[i] };
                refY = y;
            }
        }

        if (current.Any())
            rows.Add(current.OrderBy(w => w.BoundingBox.Left).ToList());

        return rows;
    }

    // Kelimeleri sütun pozisyonlarına göre hücrelere ata
    private static List<string> AssignToCols(
        List<Word> row, List<double> colPositions, double tolerance)
    {
        var cells = new string[colPositions.Count];
        for (var i = 0; i < cells.Length; i++) cells[i] = string.Empty;

        foreach (var word in row)
        {
            var wordX = word.BoundingBox.Left;
            var bestCol = 0;
            var bestDist = double.MaxValue;

            for (var i = 0; i < colPositions.Count; i++)
            {
                var dist = Math.Abs(wordX - colPositions[i]);
                if (dist < bestDist) { bestDist = dist; bestCol = i; }
            }

            // Tolerans dışındaysa yeni sütun gibi davran (son sütuna ekle)
            if (bestDist > tolerance * 3)
                cells[^1] += " " + word.Text;
            else
                cells[bestCol] += (string.IsNullOrEmpty(cells[bestCol]) ? "" : " ") + word.Text;
        }

        return cells.ToList();
    }

    // Tablo buffer'ını okunabilir metin formatına çevir
    // Her satır: "Sütun1: Sütun2: Sütun3: ..." şeklinde
    // İlk satır başlık olarak işaretlenir
    private static string FormatTable(List<List<Word>> tableRows, List<double> colPositions)
    {
        if (!tableRows.Any()) return string.Empty;

        var sb = new StringBuilder();
        sb.AppendLine("[TABLO BAŞLANGIÇ]");

        // Her satırı hücrelere çevir
        var allCells = tableRows
            .Select(row => AssignToCols(row, colPositions, ColTolerance))
            .ToList();

        var headers = allCells[0];
        var cleanHdr = headers.Select(h => h.Trim()).ToList();

        sb.AppendLine("Başlıklar: " + string.Join(" | ", cleanHdr.Where(h => !string.IsNullOrWhiteSpace(h))));

        foreach (var cells in allCells.Skip(1))
        {
            var parts = new List<string>();
            for (var i = 0; i < cells.Count; i++)
            {
                var cell = cells[i].Trim();
                if (string.IsNullOrWhiteSpace(cell)) continue;
                var header = i < cleanHdr.Count && !string.IsNullOrWhiteSpace(cleanHdr[i])
                    ? cleanHdr[i] : $"Sütun{i + 1}";
                parts.Add($"{header}: {cell}");
            }
            if (parts.Any())
                sb.AppendLine(string.Join(", ", parts));
        }

        sb.AppendLine("[TABLO BİTİŞ]");
        return sb.ToString();
    }

    // ── OCR ──────────────────────────────────────────────────────────────
    private static string OcrPage(PdfPage page, TesseractEngine engine)
    {
        try
        {
            const int dpi = 200;
            var w = (int)(page.Width / 72.0 * dpi);
            var h = (int)(page.Height / 72.0 * dpi);

            using var bmp = new System.Drawing.Bitmap(w, h);
            using var g = System.Drawing.Graphics.FromImage(bmp);
            g.Clear(System.Drawing.Color.White);

            using var font = new System.Drawing.Font("Arial", 10);
            using var brush = new System.Drawing.SolidBrush(System.Drawing.Color.Black);

            foreach (var word in page.GetWords())
            {
                var x = (float)(word.BoundingBox.Left / page.Width * w);
                var y = (float)((page.Height - word.BoundingBox.Top) / page.Height * h);
                g.DrawString(word.Text, font, brush, x, y);
            }

            using var ms = new MemoryStream();
            bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);

            using var pix = Pix.LoadFromMemory(ms.ToArray());
            using var pg = engine.Process(pix);
            return pg.GetText();
        }
        catch { return string.Empty; }
    }

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
                    case Paragraph para:
                        var line = para.InnerText.Trim();
                        if (!string.IsNullOrWhiteSpace(line))
                            sb.AppendLine(line);
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

    // DOCX tablo → okunabilir metin
    private static string ExtractDocxTable(DocumentFormat.OpenXml.Wordprocessing.Table table)
    {
        var sb = new StringBuilder();
        var rows = table.Elements<TableRow>().ToList();
        if (!rows.Any()) return string.Empty;

        sb.AppendLine("[TABLO BAŞLANGIÇ]");

        // İlk satır başlık
        var headers = rows[0].Elements<TableCell>()
            .Select(c => c.InnerText.Trim())
            .ToList();

        sb.AppendLine("Başlıklar: " + string.Join(" | ", headers.Where(h => !string.IsNullOrWhiteSpace(h))));

        // Veri satırları
        foreach (var row in rows.Skip(1))
        {
            var cells = row.Elements<TableCell>()
                .Select(c => c.InnerText.Trim())
                .ToList();

            var parts = new List<string>();
            for (var i = 0; i < cells.Count; i++)
            {
                if (string.IsNullOrWhiteSpace(cells[i])) continue;
                var header = i < headers.Count && !string.IsNullOrWhiteSpace(headers[i])
                    ? headers[i] : $"Sütun{i + 1}";
                parts.Add($"{header}: {cells[i]}");
            }

            if (parts.Any())
                sb.AppendLine(string.Join(", ", parts));
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
                sb.AppendLine($"[Sayfa: {ws.Name}]");
                var rows = ws.RowsUsed().Where(r => !r.IsHidden).ToList();
                if (!rows.Any()) continue;

                // İlk satır başlık
                var headers = rows[0].CellsUsed()
                    .Select(c => c.Value.ToString()?.Trim() ?? string.Empty)
                    .ToList();

                sb.AppendLine("Başlıklar: " + string.Join(" | ", headers.Where(h => !string.IsNullOrWhiteSpace(h))));

                foreach (var row in rows.Skip(1))
                {
                    var cells = row.CellsUsed()
                        .Select(c => c.Value.ToString()?.Trim() ?? string.Empty)
                        .ToList();

                    var parts = new List<string>();
                    for (var i = 0; i < cells.Count; i++)
                    {
                        if (string.IsNullOrWhiteSpace(cells[i])) continue;
                        var header = i < headers.Count && !string.IsNullOrWhiteSpace(headers[i])
                            ? headers[i] : $"Sütun{i + 1}";
                        parts.Add($"{header}: {cells[i]}");
                    }

                    if (parts.Any())
                        sb.AppendLine(string.Join(", ", parts));
                }
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
            using var reader = new StreamReader(stream, enc);
            using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);

            var headers = new List<string>();
            var isFirst = true;

            while (csv.Read())
            {
                if (isFirst)
                {
                    // İlk satır başlık
                    headers = Enumerable.Range(0, csv.Parser.Count)
                        .Select(i => csv.GetField(i) ?? string.Empty)
                        .ToList();
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

                if (parts.Any())
                    sb.AppendLine(string.Join(", ", parts));
            }
        }
        catch { }
        return sb.ToString();
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

    private static Encoding DetectEncoding(Stream stream)
    {
        stream.Position = 0;
        var bom = new byte[4];
        var read = 0;
        int b;
        while (read < 4 && (b = stream.ReadByte()) != -1)
            bom[read++] = (byte)b;

        if (read >= 3 && bom[0] == 0xEF && bom[1] == 0xBB && bom[2] == 0xBF)
            return new UTF8Encoding(true);
        if (read >= 2 && bom[0] == 0xFF && bom[1] == 0xFE) return Encoding.Unicode;
        if (read >= 2 && bom[0] == 0xFE && bom[1] == 0xFF) return Encoding.BigEndianUnicode;

        stream.Position = 0;
        var sample = new byte[Math.Min(stream.Length, 4096)];
        var totalRead = 0;
        int bytesRead;
        while (totalRead < sample.Length &&
               (bytesRead = stream.Read(sample, totalRead, sample.Length - totalRead)) > 0)
            totalRead += bytesRead;

        try { Encoding.UTF8.GetString(sample, 0, totalRead); return Encoding.UTF8; }
        catch { return Encoding.GetEncoding(1254); }
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

        return text.Trim();
    }

    // ── Chunk'lama ────────────────────────────────────────────────────────
    private IEnumerable<string> Chunk(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) yield break;

        var buffer = new StringBuilder();
        foreach (var sentence in SplitIntoSentences(text))
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

        var last = buffer.ToString().Trim();
        if (!string.IsNullOrWhiteSpace(last)) yield return last;
    }

    private static IEnumerable<string> SplitIntoSentences(string text)
    {
        foreach (var part in Regex.Split(text, @"(?<=[.!?])\s+|(?<=\n)\s*\n"))
        {
            var s = part.Trim();
            if (!string.IsNullOrWhiteSpace(s)) yield return s;
        }
    }
}