using DocuChat.Infrastructure.Services.Documents.Parsing.Models;
using Markdig.Extensions.Tables;
using Markdig.Renderers.Normalize;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace DocuChat.Infrastructure.Services.Documents.Parsing.StructuredExtractors;

public sealed class TableExtractor
{
    public StructuredTable Extract(Table table)
    {
        var headers = new List<string>();
        var rows = new List<IReadOnlyDictionary<string, string>>();

        var rowIndex = 0;
        foreach (var rowBlock in table.OfType<TableRow>())
        {
            var cells = rowBlock.OfType<TableCell>()
                .Select(c => CellText(c))
                .ToList();

            if (rowIndex == 0 || rowBlock.IsHeader)
            {
                if (headers.Count == 0)
                {
                    for (var i = 0; i < cells.Count; i++)
                    {
                        var h = string.IsNullOrWhiteSpace(cells[i]) ? $"col{i + 1}" : cells[i];
                        headers.Add(h);
                    }
                    rowIndex++;
                    continue;
                }
            }

            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < cells.Count && i < headers.Count; i++)
                dict[headers[i]] = cells[i];
            rows.Add(dict);
            rowIndex++;
        }

        if (headers.Count == 0)
            headers.Add("col1");

        return new StructuredTable(headers, rows);
    }

    private static string CellText(TableCell cell)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var block in cell)
            if (block is LeafBlock leaf && leaf.Inline != null)
                AppendInline(leaf.Inline, sb);
        return sb.ToString().Trim();
    }

    private static void AppendInline(ContainerInline container, System.Text.StringBuilder sb)
    {
        foreach (var inline in container)
        {
            switch (inline)
            {
                case LiteralInline lit:
                    sb.Append(lit.Content.ToString());
                    break;
                case LineBreakInline:
                    sb.Append(' ');
                    break;
                case CodeInline code:
                    sb.Append(code.Content);
                    break;
                case ContainerInline ci:
                    AppendInline(ci, sb);
                    break;
                default:
                    sb.Append(inline.ToString());
                    break;
            }
        }
    }
}
