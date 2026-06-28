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
    // Markdig pipeline — TÜM extensions explicit eklendi.
    // UseAdvancedExtensions() Bootstrap/Emoji/SmartyPants/SoftBreak hariç hepsini içerir AMA
    // belirli extension'ları manuel eklemek daha okunabilir ve gelecekteki versiyon değişimlerine
    // karşı dayanıklı. Her bir extension'ın amacı yorum satırında.
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        // Yapısal extensions (Mistral OCR çıktısında olası tüm yapılar)
        .UsePipeTables()                      // | a | b | tabloları
        .UseGridTables()                      // +---+---+ grid tabloları
        .UseListExtras()                      // a., A., i., I. ordered list (Roman/alpha)
        .UseTaskLists()                       // - [ ] checkbox
        .UseDefinitionLists()                 // term: definition
        .UseFootnotes()                       // [^1] footnote refs
        .UseCitations()                       // ""...""  akademik atıf
        .UseFigures()                         // ^^^ figure ^^^
        .UseFooters()                         // ^^ footer ^^
        .UseCustomContainers()                // :::warning ... :::
        .UseAlertBlocks()                     // > [!NOTE], > [!WARNING] GitHub alerts
        .UseMathematics()                     // $x^2$, $$\frac{a}{b}$$ LaTeX
        .UseDiagrams()                        // ```mermaid, ```plantuml
        .UseMediaLinks()                      // video/audio
        .UseAutoLinks()                       // çıplak URL otomatik link
        .UseEmphasisExtras()                  // ~~strike~~, ~sub~, ^sup^, ==mark==, ++ins++
        .UseAbbreviations()                   // *[ABBR]: definition
        .UseAutoIdentifiers()                 // heading'lere ID
        .UseGenericAttributes()               // {#id .class}
        .UseYamlFrontMatter()                 // --- YAML --- metadata
        .UseEmojiAndSmiley()                  // :smile: → 😄
        .UseSmartyPants()                     // smart quotes
        .UseGlobalization()                   // RTL support (Arabic, Hebrew, vb.)
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
            // Markdig'in node.Span → orijinal markdown'da bu block'un başlangıç ve uzunluğu.
            // Substring → birebir slice. Hiçbir re-render, hiçbir flatten yok.
            // Yeni Markdig versiyonu yeni tip eklese bile default'a düşer, raw text korunur.
            var raw = SafeSlice(markdown, node);

            switch (node)
            {
                case HeadingBlock heading:
                    // Heading kendi başına block ÜRETMEZ — sadece HeaderChain'e push.
                    // Sonraki paragraf/tablo/liste vs. bu chain altında etiketlenir.
                    tracker.Push(heading.Level, InlineToText(heading.Inline));
                    break;

                // ÖZEL TİPLER ÖNCE — pattern match base type'tan önce derived type kontrol etmeli.
                // MathBlock : FencedCodeBlock, AlertBlock : QuoteBlock, FootnoteGroup : ContainerBlock.

                case Markdig.Extensions.Mathematics.MathBlock mathBlock:
                    if (string.IsNullOrWhiteSpace(raw)) break;
                    blocks.Add(new SemanticBlock(globalIndex++, pageNumber, BlockType.Math, tracker.Current)
                    {
                        TextContent = ExtractMathContent(mathBlock),
                        RawMarkdown = raw
                    });
                    break;

                case Markdig.Extensions.Alerts.AlertBlock alertBlock:
                    if (string.IsNullOrWhiteSpace(raw)) break;
                    blocks.Add(new SemanticBlock(globalIndex++, pageNumber, BlockType.Alert, tracker.Current)
                    {
                        TextContent = ExtractContainerText(alertBlock),
                        RawMarkdown = raw
                    });
                    break;

                case Markdig.Extensions.CustomContainers.CustomContainer customContainer:
                    if (string.IsNullOrWhiteSpace(raw)) break;
                    blocks.Add(new SemanticBlock(globalIndex++, pageNumber, BlockType.Quote, tracker.Current)
                    {
                        TextContent = ExtractContainerText(customContainer),
                        RawMarkdown = raw
                    });
                    break;

                case Markdig.Extensions.DefinitionLists.DefinitionList defList:
                    if (string.IsNullOrWhiteSpace(raw)) break;
                    blocks.Add(new SemanticBlock(globalIndex++, pageNumber, BlockType.Definition, tracker.Current)
                    {
                        TextContent = ExtractAnyText(defList),
                        RawMarkdown = raw
                    });
                    break;

                case Markdig.Extensions.Figures.Figure figure:
                    if (string.IsNullOrWhiteSpace(raw)) break;
                    blocks.Add(new SemanticBlock(globalIndex++, pageNumber, BlockType.Figure, tracker.Current)
                    {
                        TextContent = ExtractContainerText(figure),
                        RawMarkdown = raw
                    });
                    break;

                case Markdig.Extensions.Footnotes.FootnoteGroup footnoteGroup:
                    foreach (var child in footnoteGroup)
                    {
                        if (child is not Markdig.Extensions.Footnotes.Footnote footnote) continue;
                        var fnRaw = SafeSlice(markdown, footnote);
                        if (string.IsNullOrWhiteSpace(fnRaw)) continue;
                        blocks.Add(new SemanticBlock(globalIndex++, pageNumber, BlockType.Footnote, tracker.Current)
                        {
                            TextContent = ExtractContainerText(footnote),
                            RawMarkdown = fnRaw
                        });
                    }
                    break;

                case Markdig.Extensions.Footers.FooterBlock footerBlock:
                    if (string.IsNullOrWhiteSpace(raw)) break;
                    blocks.Add(new SemanticBlock(globalIndex++, pageNumber, BlockType.Paragraph, tracker.Current)
                    {
                        TextContent = ExtractContainerText(footerBlock),
                        RawMarkdown = raw
                    });
                    break;

                case Markdig.Extensions.Yaml.YamlFrontMatterBlock yamlBlock:
                    if (string.IsNullOrWhiteSpace(raw)) break;
                    blocks.Add(new SemanticBlock(globalIndex++, pageNumber, BlockType.YamlFrontMatter, tracker.Current)
                    {
                        TextContent = raw,
                        RawMarkdown = raw
                    });
                    break;

                case Table table:
                {
                    // ATOMİK: chunker'ın tabloyu bölmemesi için StructuredTable lazım.
                    // Aynı zamanda RawMarkdown ile orijinal pipe-table formatı korunur.
                    blocks.Add(new SemanticBlock(globalIndex++, pageNumber, BlockType.Table, tracker.Current)
                    {
                        Table = _tableExtractor.Extract(table),
                        RawMarkdown = raw,
                        TextContent = raw  // fallback için (TableToCleanText ayrı handle eder)
                    });
                    break;
                }

                case ListBlock list:
                {
                    // ListItems: chunker'ın item-bazlı split için (dev liste 800 token'ı aşarsa).
                    // RawMarkdown: orijinal liste yapısı (NESTED dahil — bold/italic/inline link).
                    var (items, ordered) = ExtractListItems(list);
                    if (items.Count == 0 && string.IsNullOrWhiteSpace(raw)) break;
                    var b = new SemanticBlock(globalIndex++, pageNumber, BlockType.List, tracker.Current)
                    {
                        IsOrdered = ordered,
                        RawMarkdown = raw,
                        TextContent = NormalizeWhitespace(string.Join(" ", items))
                    };
                    b.ListItems.AddRange(items);
                    blocks.Add(b);
                    break;
                }

                case QuoteBlock quote:
                {
                    if (string.IsNullOrWhiteSpace(raw)) break;
                    var text = ExtractContainerText(quote);
                    blocks.Add(new SemanticBlock(globalIndex++, pageNumber, BlockType.Quote, tracker.Current)
                    {
                        TextContent = text,
                        RawMarkdown = raw
                    });
                    break;
                }

                case FencedCodeBlock:
                {
                    if (string.IsNullOrWhiteSpace(raw)) break;
                    // Code: RawMarkdown zaten ```lang\n...\n``` fence'iyle birlikte gelir.
                    blocks.Add(new SemanticBlock(globalIndex++, pageNumber, BlockType.Code, tracker.Current)
                    {
                        TextContent = ExtractCodeContent(node),  // sadece içerik (fence'siz, embedding için)
                        RawMarkdown = raw
                    });
                    break;
                }

                case ThematicBreakBlock:
                    blocks.Add(new SemanticBlock(globalIndex++, pageNumber, BlockType.ThematicBreak, tracker.Current)
                    {
                        TextContent = "---",
                        RawMarkdown = string.IsNullOrWhiteSpace(raw) ? "---" : raw
                    });
                    break;

                default:
                {
                    // ParagraphBlock, HtmlBlock, vs. → ParagraphBlock olarak emit.
                    if (string.IsNullOrWhiteSpace(raw)) break;
                    var text = ExtractAnyText(node);

                    // SAYFA NUMARASI TESPİTİ (AST-based):
                    // ParagraphBlock + sadece tek inline + tamamen rakam → Mistral'in sayfa numarası.
                    // Ham metin ise PageNumber metadata'da zaten var, içerikte gürültü olmasın.
                    if (node is ParagraphBlock para && IsPageNumberOnly(para, text))
                        break;

                    blocks.Add(new SemanticBlock(globalIndex++, pageNumber, BlockType.Paragraph, tracker.Current)
                    {
                        TextContent = text,
                        RawMarkdown = raw
                    });
                    break;
                }
            }
        }

        return blocks;
    }

    private static string SafeSlice(string source, Markdig.Syntax.Block node)
    {
        if (string.IsNullOrEmpty(source)) return string.Empty;
        var start = node.Span.Start;
        var length = node.Span.Length;
        if (start < 0 || length <= 0 || start >= source.Length) return string.Empty;
        if (start + length > source.Length) length = source.Length - start;
        return source.Substring(start, length).TrimEnd();
    }

    private static string ExtractCodeContent(Markdig.Syntax.Block block)
    {
        if (block is FencedCodeBlock fenced && fenced.Lines.Lines != null)
            return string.Join("\n", fenced.Lines.Lines.Select(l => l.Slice.ToString())).TrimEnd();
        return string.Empty;
    }

    private static string ExtractMathContent(Markdig.Extensions.Mathematics.MathBlock mathBlock)
    {
        if (mathBlock.Lines.Lines == null) return string.Empty;
        return string.Join("\n", mathBlock.Lines.Lines.Select(l => l.Slice.ToString())).TrimEnd();
    }

    /// <summary>
    /// ParagraphBlock'un sadece bir sayfa numarası olup olmadığını AST üzerinden tespit eder.
    /// Koşullar (hepsi sağlanmalı):
    ///   - Tek bir LiteralInline child'ı var
    ///   - Trim edilmiş text 1-4 karakter
    ///   - Tüm karakterler digit (0-9)
    /// Mistral OCR sayfa başına/sonuna "1", "2"... gibi sayfa numarası ekleyebiliyor.
    /// PageNumber metadata'da zaten var → content'te tekrarlama gereksiz gürültü.
    /// </summary>
    private static bool IsPageNumberOnly(ParagraphBlock paragraph, string extractedText)
    {
        // Trim edilmiş text 1-4 karakter ve hepsi digit mi?
        var trimmed = extractedText.Trim();
        if (trimmed.Length == 0 || trimmed.Length > 4) return false;
        foreach (var ch in trimmed)
            if (!char.IsDigit(ch)) return false;

        // AST kontrolü: paragraph'ta yalnız tek bir LiteralInline olmalı
        if (paragraph.Inline is null) return false;
        var inlineCount = 0;
        var hasOnlyLiteral = true;
        foreach (var inline in paragraph.Inline)
        {
            inlineCount++;
            if (inline is not LiteralInline) hasOnlyLiteral = false;
            if (inlineCount > 1) break;
        }
        return inlineCount == 1 && hasOnlyLiteral;
    }

    private static string ExtractAnyText(Markdig.Syntax.Block block)
    {
        var sb = new StringBuilder();
        CollectBlockText(block, sb);
        return NormalizeWhitespace(sb.ToString());
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
