using System.Text;
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
        _chunkSize = int.Parse(cfg["Chunking:ChunkSize"] ?? "800");
        _overlap = int.Parse(cfg["Chunking:Overlap"] ?? "100");
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

        return Chunk(text);
    }

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
            sb.AppendLine(para.InnerText);
        return sb.ToString();
    }

    private static string ExtractXlsx(Stream stream)
    {
        var sb = new StringBuilder();
        using var wb = new XLWorkbook(stream);
        foreach (var ws in wb.Worksheets)
        {
            sb.AppendLine($"[Sheet: {ws.Name}]");
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

    private IEnumerable<string> Chunk(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) yield break;

        var start = 0;
        while (start < text.Length)
        {
            var length = Math.Min(_chunkSize, text.Length - start);
            yield return text.Substring(start, length);
            start += _chunkSize - _overlap;
        }
    }
}