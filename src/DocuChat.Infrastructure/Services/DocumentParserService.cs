using System.Text;
using System.Text.RegularExpressions;
using DocuChat.Application.Abstractions;
using DocuChat.Domain.Enums;
using UglyToad.PdfPig;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using ClosedXML.Excel;
using CsvHelper;
using System.Globalization;

namespace DocuChat.Infrastructure.Services;

public class DocumentParserService : IDocumentParser
{
    private readonly int _chunkSize;
    private readonly int _overlap;

    public DocumentParserService(Microsoft.Extensions.Configuration.IConfiguration cfg)
    {
        _chunkSize = int.Parse(cfg["Chunking:ChunkSize"] ?? "1200");
        _overlap = int.Parse(cfg["Chunking:Overlap"] ?? "200");
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

    // ── Extractors ───────────────────────────────────────────────────────

    private static string ExtractPdf(Stream stream)
    {
        var sb = new StringBuilder();
        using var doc = PdfDocument.Open(stream);
        foreach (var page in doc.GetPages())
            sb.AppendLine(page.Text);
        return sb.ToString();
    }

    private static string ExtractDocx(Stream stream)
    {
        var sb = new StringBuilder();
        using var doc = WordprocessingDocument.Open(stream, false);
        var body = doc.MainDocumentPart?.Document?.Body;
        if (body is null) return string.Empty;

        foreach (var para in body.Elements<Paragraph>())
        {
            var line = para.InnerText.Trim();
            if (!string.IsNullOrWhiteSpace(line))
                sb.AppendLine(line);
        }

        return sb.ToString();
    }

    private static string ExtractXlsx(Stream stream)
    {
        var sb = new StringBuilder();
        using var wb = new XLWorkbook(stream);
        foreach (var ws in wb.Worksheets)
        {
            sb.AppendLine($"[Sayfa: {ws.Name}]");
            foreach (var row in ws.RowsUsed())
            {
                var cells = row.CellsUsed().Select(c => c.Value.ToString());
                sb.AppendLine(string.Join("\t", cells));
            }
        }
        return sb.ToString();
    }

    private static string ExtractCsv(Stream stream)
    {
        using var reader = new StreamReader(stream);
        using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);
        var sb = new StringBuilder();
        while (csv.Read())
        {
            var fields = Enumerable.Range(0, csv.Parser.Count)
                                   .Select(i => csv.GetField(i) ?? string.Empty);
            sb.AppendLine(string.Join("\t", fields));
        }
        return sb.ToString();
    }

    private static string ExtractTxt(Stream stream)
    {
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    // ── Temizleme ─────────────────────────────────────────────────────────

    private static string CleanText(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        // Box-drawing karakterleri
        text = Regex.Replace(text, @"[│├└─┌┐┘┤┬┴┼╔╗╚╝╠╣╦╩╬►◄▲▼]", " ");

        // Birden fazla boşluk → tek boşluk
        text = Regex.Replace(text, @"[ \t]{2,}", " ");

        // 3'ten fazla ardışık newline → 2'ye indir
        text = Regex.Replace(text, @"(\r?\n){3,}", "\n\n");

        // Satır başı/sonu boşlukları
        text = string.Join("\n", text.Split('\n').Select(l => l.Trim()));

        text = text.Replace("\r", string.Empty);

        return text.Trim();
    }

    // ── Cümle-bilinçli Chunk'lama ─────────────────────────────────────────
    //
    // Eski yöntem: text.Substring(start, chunkSize)
    //   → cümlenin ortasından keser, "Madde 13 – kapal..." gibi yarım chunk'lar oluşur.
    //
    // Yeni yöntem:
    //   1. Metni cümlelere böl (nokta/satır sonu sınırı).
    //   2. Cümleleri _chunkSize dolana kadar biriktir.
    //   3. Chunk dolduğunda yaz; overlap için son N karakteri bir sonrakine taşı.
    //   Bu sayede hiçbir cümle ikiye bölünmez.

    private IEnumerable<string> Chunk(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) yield break;

        // Cümle sınırlarını bul: ". ", ".\n", "! ", "? ", "\n\n"
        var sentences = SplitIntoSentences(text);

        var buffer = new StringBuilder();
        string? overlapTail = null;   // bir önceki chunk'ın son _overlap karakteri

        foreach (var sentence in sentences)
        {
            // Eğer bu cümleyi eklemek chunk'ı taşırsa, mevcut buffer'ı yay
            if (buffer.Length > 0 &&
                buffer.Length + sentence.Length > _chunkSize)
            {
                var chunk = buffer.ToString().Trim();
                if (!string.IsNullOrWhiteSpace(chunk))
                    yield return chunk;

                // Overlap: chunk'ın sonundan _overlap karakter al
                overlapTail = chunk.Length > _overlap
                    ? chunk[^_overlap..]
                    : chunk;

                buffer.Clear();

                // Yeni buffer'a overlap'i ekle
                if (!string.IsNullOrWhiteSpace(overlapTail))
                    buffer.Append(overlapTail).Append(' ');
            }

            buffer.Append(sentence).Append(' ');
        }

        // Kalan buffer'ı yay
        var last = buffer.ToString().Trim();
        if (!string.IsNullOrWhiteSpace(last))
            yield return last;
    }

    // Metni cümlelere böler; paragraf sonlarını da sınır olarak kabul eder.
    private static IEnumerable<string> SplitIntoSentences(string text)
    {
        // Sınır: nokta/ünlem/soru işareti + boşluk/newline  VEYA  çift newline (paragraf)
        var parts = Regex.Split(text, @"(?<=[.!?])\s+|(?<=\n)\s*\n");

        foreach (var part in parts)
        {
            var s = part.Trim();
            if (!string.IsNullOrWhiteSpace(s))
                yield return s;
        }
    }
}