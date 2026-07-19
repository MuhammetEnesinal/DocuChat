using System.Text;
using DocuChat.Application.Interfaces.Services.Ai.Embedding;
using DocuChat.Application.Interfaces.Services.Ai.Llm;
using DocuChat.Application.Interfaces.Services.Ai.Reranker;
using DocuChat.Application.Interfaces.Services.Ai.Retrieval;
using DocuChat.Application.Interfaces.Services.Documents;
using DocuChat.Application.Interfaces.Services.Auth;
using DocuChat.Application.Interfaces.Services.UserManagement;
using DocuChat.Application.Interfaces.Services.Email;
using DocuChat.Application.Interfaces.Services.Storage;
using DocuChat.Application.Interfaces.Services.Persistence;
using DocuChat.Application.ServiceContracts;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Microsoft.SemanticKernel.Text;

namespace DocuChat.Infrastructure.Services.Documents.Parsing.Chunking;

// Sayfa-bazlı chunker — Markdig AST + Microsoft TextChunker.
// FELSEFE: Sıfır manuel kural, sıfır regex, sıfır boyut eşiği.
// - Yapısal kararlar Markdig AST'ten gelir (heading, table, list, paragraph node tipleri)
// - Boyut kararları Microsoft TextChunker'a delege edilir (markdown-aware splitting)
// - Hiçbir içerik silinmez veya pattern eşleşmesine göre filtrelenmez
// - Tek "marker" işlemi: [IMG_PATH:abc] → [IMG:N] (bizim teknik marker'ımız)
// AKIŞ (her sayfa için):
// 1. PdfPig fallback görselleri sayfa sonuna [IMG_PATH:...] olarak append
// 2. Markdown TextChunker'a verilir → token-aware chunks (markdown-aware boundaries)
// 3. Her chunk için Markdig AST ile section heading bulunur (regex değil, AST)
// 4. [IMG_PATH:...] → [IMG:N] renumber (chunk-yerel)
// 5. CleanContent (markdown syntax temizlenmiş, embedding + BM25 için)
public sealed class PageBasedChunker
{
    private readonly ITokenCounter _tokens;
    private readonly int _maxTokensPerChunk;
    private readonly int _overlapTokens;

    private static readonly MarkdownPipeline MarkdigPipeline = new MarkdownPipelineBuilder()
        .UsePipeTables()
        .UseGridTables()
        .UseAutoLinks()
        .UseEmphasisExtras()
        .Build();

    public PageBasedChunker(ITokenCounter tokens, int maxTokensPerChunk = 512, int overlapTokens = 0)
    {
        _tokens = tokens;
        _maxTokensPerChunk = maxTokensPerChunk;
        _overlapTokens = overlapTokens;
    }

    public List<ParsedChunk> Chunk(IReadOnlyList<PageInput> pages)
    {
        var result = new List<ParsedChunk>();

        foreach (var page in pages)
        {
            var markdown = page.Markdown ?? string.Empty;
            if (string.IsNullOrWhiteSpace(markdown)) continue;

            // PdfPig fallback görsellerini sayfa sonuna [IMG_PATH:...] olarak append et.
            // (Mistral inline'da olmayan embedded görseller burada toparlanır.)
            if (page.FallbackImagePaths.Count > 0)
            {
                var sb = new StringBuilder(markdown.TrimEnd());
                sb.AppendLine();
                foreach (var p in page.FallbackImagePaths)
                    sb.Append("[IMG_PATH:").Append(p).AppendLine("]");
                markdown = sb.ToString();
            }

            // TextChunker'a markdown'ı doğrudan ver — sayfa içi pre-processing YOK.
            // Microsoft markdown-aware: tablo satırı, liste maddesi, başlık, code fence
            // hepsi line boundary'sinde korunur. Cümle ortasından kesme yok.
            var lines = TextChunker.SplitMarkDownLines(markdown, _maxTokensPerChunk, CountTokens);
            var paragraphs = TextChunker.SplitMarkdownParagraphs(
                lines,
                maxTokensPerParagraph: _maxTokensPerChunk,
                overlapTokens: _overlapTokens,
                chunkHeader: null,
                tokenCounter: CountTokens);

            // Page-level "carry-forward" heading: ilk chunk'ta başlık varsa, sonraki
            // chunk'larda başlık yoksa son görülen başlık taşınır. AST-based, regex yok.
            string? lastHeadingText = null;

            foreach (var chunk in paragraphs)
            {
                var chunkHeading = ExtractFirstHeadingFromAst(chunk);
                if (chunkHeading != null) lastHeadingText = chunkHeading;

                result.Add(BuildChunk(chunk, page.PageNumber, chunkHeading ?? lastHeadingText));
            }
        }

        return result;
    }

    // Markdig AST kullanarak chunk içindeki ilk HeadingBlock'un text'ini çıkar.
    // Regex YOK — sadece AST node tipi kontrolü.
    private static string? ExtractFirstHeadingFromAst(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return null;
        var doc = Markdown.Parse(markdown, MarkdigPipeline);
        foreach (var node in doc)
        {
            if (node is HeadingBlock heading)
                return CollectInlineText(heading.Inline);
        }
        return null;
    }

    // Markdig ContainerInline'dan düz text çıkar (AST traversal, regex yok).
    private static string? CollectInlineText(ContainerInline? container)
    {
        if (container == null) return null;
        var sb = new StringBuilder();
        AppendInline(container, sb);
        var result = sb.ToString().Trim();
        return result.Length > 0 ? result : null;
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
                case ContainerInline ci:
                    AppendInline(ci, sb);
                    break;
            }
        }
    }

    private ParsedChunk BuildChunk(string rawMarkdown, int pageNumber, string? header)
    {
        // [IMG_PATH:abc] → [IMG:1] (chunk-yerel numbering)
        var (content, paths) = RenumberImageMarkers(rawMarkdown.Trim());

        // CleanContent: marker'sız + markdown syntax temizlenmiş (embedding + BM25 için)
        // Markdig kullanarak AST-based plain text extraction (regex yok).
        var clean = MakeCleanTextFromAst(content);

        var imagePathJson = paths.Count > 0
            ? System.Text.Json.JsonSerializer.Serialize(paths)
            : null;

        return new ParsedChunk(
            Content: content,
            ImagePath: imagePathJson,
            Header: string.IsNullOrWhiteSpace(header) ? null : header,
            CleanContent: clean,
            PageNumber: pageNumber > 0 ? pageNumber : null);
    }

    // [IMG_PATH:abc] markerlarını [IMG:N] formuna çevirir (chunk-yerel).
    // Aynı path → aynı numara (path dedup). Bu BİZİM teknik marker'ımız —
    // belge içeriği değil, pipeline aşamaları arası geçici işaret.
    private static (string Text, List<string> Paths) RenumberImageMarkers(string text)
    {
        if (string.IsNullOrEmpty(text)) return (text, new List<string>());

        var paths = new List<string>();
        var idxByPath = new Dictionary<string, int>();
        var sb = new StringBuilder(text.Length);

        var i = 0;
        while (i < text.Length)
        {
            // "[IMG_PATH:" öneki aranır — düz metin karşılaştırması, regex kullanılmaz.
            if (i <= text.Length - 10 && text.AsSpan(i, 10).SequenceEqual("[IMG_PATH:"))
            {
                var close = text.IndexOf(']', i + 10);
                if (close > i)
                {
                    var path = text.Substring(i + 10, close - i - 10).Trim();
                    if (!idxByPath.TryGetValue(path, out var n))
                    {
                        paths.Add(path);
                        n = paths.Count;
                        idxByPath[path] = n;
                    }
                    sb.Append("[IMG:").Append(n).Append(']');
                    i = close + 1;
                    continue;
                }
            }
            sb.Append(text[i]);
            i++;
        }

        return (sb.ToString(), paths);
    }

    // Markdig AST üzerinden düz text çıkarımı — embedding + BM25 için.
    // Hiçbir regex yok, hiçbir karakter sınıfı kuralı yok. Markdown sözdizimi
    // (|, *, #, vb.) AST tarafından doğal olarak elimine edilir, sadece içerik kalır.
    private static string MakeCleanTextFromAst(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return string.Empty;

        var doc = Markdown.Parse(markdown, MarkdigPipeline);
        var sb = new StringBuilder();
        CollectBlockText(doc, sb);

        // [IMG:N] markerlarını çıkar — bizim teknik marker, embedding'e girmesin
        var result = sb.ToString();
        return NormalizeWhitespace(StripImgMarkers(result));
    }

    private static void CollectBlockText(Block block, StringBuilder sb)
    {
        switch (block)
        {
            case LeafBlock leaf when leaf.Inline != null:
                AppendInline(leaf.Inline, sb);
                sb.Append(' ');
                break;
            case Markdig.Extensions.Tables.Table table:
                foreach (var rowBlock in table)
                {
                    if (rowBlock is Markdig.Extensions.Tables.TableRow row)
                    {
                        foreach (var cell in row)
                        {
                            if (cell is Markdig.Extensions.Tables.TableCell tc)
                                CollectBlockText(tc, sb);
                        }
                    }
                }
                break;
            case Markdig.Syntax.FencedCodeBlock fenced:
                if (fenced.Lines.Lines != null)
                {
                    foreach (var line in fenced.Lines.Lines)
                    {
                        sb.Append(line.Slice.ToString());
                        sb.Append(' ');
                    }
                }
                break;
            case ContainerBlock container:
                foreach (var child in container)
                    CollectBlockText(child, sb);
                break;
        }
    }

    private static string StripImgMarkers(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        var sb = new StringBuilder(s.Length);
        var i = 0;
        while (i < s.Length)
        {
            if (i <= s.Length - 5 && s.AsSpan(i, 5).SequenceEqual("[IMG:"))
            {
                var close = s.IndexOf(']', i + 5);
                if (close > i)
                {
                    sb.Append(' ');
                    i = close + 1;
                    continue;
                }
            }
            sb.Append(s[i]);
            i++;
        }
        return sb.ToString();
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

    // TextChunker'a delegat olarak verilen token counter wrapper.
    private int CountTokens(string text) => _tokens.Count(text ?? string.Empty);
}

// PageBasedChunker'ın input sınıfı — Mistral OCR çıktısı + PdfPig fallback görselleri.
public class PageInput
{
    public int PageNumber { get; set; }
    public string Markdown { get; set; }
    public IReadOnlyList<string> FallbackImagePaths { get; set; }

    public PageInput(int PageNumber, string Markdown, IReadOnlyList<string> FallbackImagePaths)
    {
        this.PageNumber = PageNumber;
        this.Markdown = Markdown;
        this.FallbackImagePaths = FallbackImagePaths;
    }
}
