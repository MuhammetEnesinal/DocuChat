using System.Text;
using DocuChat.Infrastructure.Services.Documents.Parsing.Models;
using DocuChat.Infrastructure.Services.Documents.Parsing.StructuredExtractors;
using Markdig;
using Markdig.Extensions.Tables;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace DocuChat.Infrastructure.Services.Documents.Parsing.Ast;

public sealed class MarkdownBlockExtractor
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UsePipeTables()
        .UseGridTables()
        .UseAutoLinks()
        .UseEmphasisExtras()
        .Build();

    private readonly TableExtractor _tableExtractor;

    public MarkdownBlockExtractor(TableExtractor tableExtractor)
    {
        _tableExtractor = tableExtractor;
    }

    public List<SemanticBlock> Extract(string markdown, int pageNumber, HeaderChainTracker tracker, ref int globalIndex)
    {
        var blocks = new List<SemanticBlock>();
        if (string.IsNullOrWhiteSpace(markdown)) return blocks;

        var doc = Markdown.Parse(markdown, Pipeline);

        foreach (var node in doc)
        {
            switch (node)
            {
                case HeadingBlock heading:
                    tracker.Push(heading.Level, InlineToText(heading.Inline));
                    break;

                case Table table:
                {
                    blocks.Add(new SemanticBlock(globalIndex++, pageNumber, BlockType.Table, tracker.Current)
                    {
                        Table = _tableExtractor.Extract(table)
                    });
                    break;
                }

                case ListBlock list:
                {
                    var (items, ordered) = ExtractListItems(list);
                    if (items.Count == 0) break;
                    var b = new SemanticBlock(globalIndex++, pageNumber, BlockType.List, tracker.Current)
                    {
                        IsOrdered = ordered
                    };
                    b.ListItems.AddRange(items);
                    blocks.Add(b);
                    break;
                }

                case QuoteBlock quote:
                {
                    var text = ExtractContainerText(quote);
                    if (string.IsNullOrWhiteSpace(text)) break;
                    blocks.Add(new SemanticBlock(globalIndex++, pageNumber, BlockType.Quote, tracker.Current)
                    {
                        TextContent = text
                    });
                    break;
                }

                case FencedCodeBlock fenced:
                {
                    var lines = fenced.Lines.Lines;
                    if (lines == null) break;
                    var code = string.Join("\n", lines.Select(l => l.Slice.ToString()));
                    if (string.IsNullOrWhiteSpace(code)) break;
                    blocks.Add(new SemanticBlock(globalIndex++, pageNumber, BlockType.Code, tracker.Current)
                    {
                        TextContent = code.TrimEnd()
                    });
                    break;
                }

                case ParagraphBlock para:
                {
                    var text = InlineToText(para.Inline);
                    if (string.IsNullOrWhiteSpace(text)) break;
                    blocks.Add(new SemanticBlock(globalIndex++, pageNumber, BlockType.Paragraph, tracker.Current)
                    {
                        TextContent = text
                    });
                    break;
                }

                case ThematicBreakBlock:
                    break;

                default:
                {
                    // Bilinmeyen tip — text içeriği çıkarıp paragraf olarak ekle
                    var sb = new StringBuilder();
                    CollectBlockText(node, sb);
                    var text = NormalizeWhitespace(sb.ToString());
                    if (string.IsNullOrWhiteSpace(text)) break;
                    blocks.Add(new SemanticBlock(globalIndex++, pageNumber, BlockType.Paragraph, tracker.Current)
                    {
                        TextContent = text
                    });
                    break;
                }
            }
        }

        return blocks;
    }

    // AST yürüyücüler — hiç regex yok, sadece tip kontrolü

    private static (List<string> Items, bool IsOrdered) ExtractListItems(ListBlock list)
    {
        var items = new List<string>();
        foreach (var child in list)
        {
            if (child is not ListItemBlock item) continue;
            var sb = new StringBuilder();
            CollectBlockText(item, sb);
            var text = NormalizeWhitespace(sb.ToString());
            if (!string.IsNullOrWhiteSpace(text)) items.Add(text);
        }
        return (items, list.IsOrdered);
    }

    private static string ExtractContainerText(ContainerBlock container)
    {
        var sb = new StringBuilder();
        CollectBlockText(container, sb);
        return NormalizeWhitespace(sb.ToString());
    }

    private static void CollectBlockText(Block block, StringBuilder sb)
    {
        switch (block)
        {
            case LeafBlock leaf when leaf.Inline != null:
                AppendInline(leaf.Inline, sb);
                sb.Append(' ');
                break;
            case FencedCodeBlock fenced:
                var fLines = fenced.Lines.Lines;
                if (fLines == null) break;
                foreach (var line in fLines)
                {
                    sb.Append(line.Slice.ToString());
                    sb.Append(' ');
                }
                break;
            case ContainerBlock container:
                foreach (var child in container)
                    CollectBlockText(child, sb);
                break;
        }
    }

    private static string InlineToText(ContainerInline? inline)
    {
        if (inline == null) return string.Empty;
        var sb = new StringBuilder();
        AppendInline(inline, sb);
        return NormalizeWhitespace(sb.ToString());
    }

    private static void AppendInline(ContainerInline container, StringBuilder sb)
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
                case AutolinkInline auto:
                    sb.Append(auto.Url);
                    break;
                case ContainerInline ci:
                    AppendInline(ci, sb);
                    break;
            }
        }
    }

    private static string NormalizeWhitespace(string s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        var sb = new StringBuilder(s.Length);
        var prevSpace = false;
        foreach (var ch in s)
        {
            if (ch == ' ' || ch == '\t' || ch == '\r' || ch == '\n')
            {
                if (!prevSpace) sb.Append(' ');
                prevSpace = true;
            }
            else { sb.Append(ch); prevSpace = false; }
        }
        return sb.ToString().Trim();
    }
}
