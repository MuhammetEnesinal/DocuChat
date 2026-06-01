using System.Text;
using DocuChat.Infrastructure.Services.Documents.Parsing.Models;

namespace DocuChat.Infrastructure.Services.Documents.Parsing.Rendering;

public sealed class MarkdownRenderer : IMarkdownRenderer
{
    public string Render(SemanticBlock block) => block.Type switch
    {
        BlockType.Paragraph => RenderTextBlock(block),
        BlockType.Quote     => RenderQuote(block),
        BlockType.Code      => RenderCode(block),
        BlockType.List      => RenderList(block),
        BlockType.Table     => RenderTable(block),
        _                   => RenderTextBlock(block)
    };

    public string Render(IEnumerable<SemanticBlock> blocks)
    {
        var sb = new StringBuilder();
        var first = true;
        foreach (var b in blocks)
        {
            if (!first) sb.Append("\n\n");
            sb.Append(Render(b));
            first = false;
        }
        return sb.ToString();
    }

    public string ToCleanText(SemanticBlock block) => block.Type switch
    {
        BlockType.Table => TableToCleanText(block.Table),
        BlockType.List  => string.Join(" ", block.ListItems),
        _               => block.TextContent
    };

    // Block tipi başına renderer

    private static string RenderTextBlock(SemanticBlock block)
    {
        var sb = new StringBuilder(block.TextContent);
        AppendImageMarkers(sb, block.Images);
        return sb.ToString();
    }

    private static string RenderQuote(SemanticBlock block)
    {
        var sb = new StringBuilder();
        foreach (var line in block.TextContent.Split('\n'))
            sb.Append("> ").AppendLine(line);
        AppendImageMarkers(sb, block.Images);
        return sb.ToString().TrimEnd();
    }

    private static string RenderCode(SemanticBlock block)
    {
        var sb = new StringBuilder();
        sb.AppendLine("```");
        sb.AppendLine(block.TextContent);
        sb.Append("```");
        // Image marker'ı code fence'in dışına ekle
        if (block.Images.Count > 0)
        {
            sb.AppendLine();
            AppendImageMarkers(sb, block.Images);
        }
        return sb.ToString();
    }

    private static string RenderList(SemanticBlock block)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < block.ListItems.Count; i++)
        {
            var prefix = block.IsOrdered ? $"{i + 1}. " : "- ";
            sb.Append(prefix).AppendLine(block.ListItems[i]);
        }
        AppendImageMarkers(sb, block.Images);
        return sb.ToString().TrimEnd();
    }

    private static string RenderTable(SemanticBlock block)
    {
        var table = block.Table;
        if (table == null || table.Headers.Count == 0) return string.Empty;

        var sb = new StringBuilder();

        // Header
        sb.Append('|');
        foreach (var h in table.Headers) sb.Append(' ').Append(EscapeCell(h)).Append(" |");
        sb.AppendLine();

        // Separator
        sb.Append('|');
        foreach (var _ in table.Headers) sb.Append(" --- |");
        sb.AppendLine();

        // Data
        foreach (var row in table.Rows)
        {
            sb.Append('|');
            for (var c = 0; c < table.Headers.Count; c++)
            {
                var colName = table.Headers[c];
                var cell = row.TryGetValue(colName, out var v) ? v ?? string.Empty : string.Empty;
                sb.Append(' ').Append(EscapeCell(cell)).Append(" |");
            }
            sb.AppendLine();
        }

        // Image marker'ları her zaman tablo altına append.
        AppendImageMarkers(sb, block.Images);

        return sb.ToString().TrimEnd();
    }

    private static void AppendImageMarkers(StringBuilder sb, IReadOnlyList<ImageWithBbox> images)
    {
        if (images.Count == 0) return;
        if (sb.Length > 0 && sb[^1] != '\n') sb.AppendLine();
        foreach (var img in images)
            sb.Append("[IMG_PATH:").Append(img.Path).Append(']').AppendLine();
    }

    private static string EscapeCell(string s) =>
        string.IsNullOrEmpty(s) ? string.Empty : s.Replace("|", "\\|").Replace("\n", " ").Trim();

    private static string TableToCleanText(StructuredTable? table)
    {
        if (table == null) return string.Empty;
        var sb = new StringBuilder();
        sb.Append(string.Join(" ", table.Headers));
        foreach (var row in table.Rows)
        {
            sb.Append(' ');
            sb.Append(string.Join(" ", row.Values));
        }
        return sb.ToString().Trim();
    }
}
