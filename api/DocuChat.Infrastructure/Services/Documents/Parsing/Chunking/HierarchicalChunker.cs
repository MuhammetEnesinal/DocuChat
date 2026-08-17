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
using DocuChat.Infrastructure.Services.Documents.Parsing.Models;
using DocuChat.Infrastructure.Services.Documents.Parsing.Rendering;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Microsoft.SemanticKernel.Text;

namespace DocuChat.Infrastructure.Services.Documents.Parsing.Chunking;

// HierarchicalChunker — sektör standardı RAG chunker.
// FELSEFE: Sıfır regex, sıfır karakter eşiği, sıfır manuel desen detection.
// - Yapısal kararlar: Markdig AST node tipleri (HeadingBlock, Table, ListBlock, ...)
// - Boyut yönetimi: Microsoft Semantic Kernel TextChunker (oversized text için)
// - Header chain: HeaderChainTracker stack (H1 > H2 > H3 hierarşi)
// - Atomic preservation: Table & Code asla ortadan bölünmez
// MİMARİ:
// 1. AST'ten gelen SemanticBlock'lar → HeaderChain'e göre section'lara grupla
// 2. Her section:
// - total tokens ≤ maxTokens → 1 chunk (tüm yapı bir arada)
// - total tokens > maxTokens → atomic-aware split:
// • Table/Code → kendi chunk'ı (atomic)
// • Text birikimi → token bütçesinde flush
// • Tek block > maxTokens → TextChunker fallback (line-aware)
// 3. Her chunk başına HeaderChain prepend (LLM context için)
// 4. [IMG_PATH:N] → [IMG:N] renumber (chunk-yerel, path dedup)
// 5. CleanContent: Markdig AST text extraction (regex yok)
public sealed class HierarchicalChunker
{
    private readonly ITokenCounter _tokens;
    private readonly IMarkdownRenderer _renderer;
    private readonly int _maxTokensPerChunk;

    private static readonly MarkdownPipeline MarkdigPipeline = new MarkdownPipelineBuilder()
        .UsePipeTables()
        .UseGridTables()
        .UseAutoLinks()
        .UseEmphasisExtras()
        .Build();

    public HierarchicalChunker(ITokenCounter tokens, IMarkdownRenderer renderer, int maxTokensPerChunk = 512)
    {
        _tokens = tokens;
        _renderer = renderer;
        _maxTokensPerChunk = maxTokensPerChunk;
    }

    public List<ParsedChunk> Chunk(IReadOnlyList<SemanticBlock> blocks)
    {
        if (blocks.Count == 0) return new List<ParsedChunk>();

        var result = new List<ParsedChunk>();

        // Header chain'e göre ardışık section'lar
        var sections = GroupByHeaderChain(blocks);

        foreach (var section in sections)
        {
            var headerPath = section[0].Headers.ToPath();
            var sectionTokens = section.Sum(b => _tokens.Count(_renderer.ToCleanText(b)));

            if (sectionTokens <= _maxTokensPerChunk)
            {
                // Section bütün — tek chunk
                result.Add(BuildChunk(section, headerPath));
                continue;
            }

            // Section büyük — atomic-aware split
            result.AddRange(SplitSection(section, headerPath));
        }

        return result;
    }

    // Ardışık SemanticBlock'ları aynı HeaderChain.ToPath() altında grupla.
    // HeadingBlock'lar zaten block üretmez (sadece tracker'a push), boş heading
    // section'ları otomatik olarak bir sonraki content-bearing section'ın HeaderChain'ine
    // dahil edilir. AST-driven, manuel "trivial" tespiti gerekmez.
    private static List<List<SemanticBlock>> GroupByHeaderChain(IReadOnlyList<SemanticBlock> blocks)
    {
        var groups = new List<List<SemanticBlock>>();
        if (blocks.Count == 0) return groups;

        var current = new List<SemanticBlock> { blocks[0] };
        var currentPath = blocks[0].Headers.ToPath();

        for (var i = 1; i < blocks.Count; i++)
        {
            var path = blocks[i].Headers.ToPath();
            if (path == currentPath)
            {
                current.Add(blocks[i]);
            }
            else
            {
                groups.Add(current);
                current = new List<SemanticBlock> { blocks[i] };
                currentPath = path;
            }
        }
        groups.Add(current);
        return groups;
    }

    // Section maxTokens'ı aşıyorsa atomic-aware split uygular:
    // - Table/Code → kendi chunk'ı (asla bölünmez, sadece son çare aşırı büyükse TextChunker)
    // - Diğer tipler → token bütçesinde birikir, taşarsa flush
    // - Tek block > maxTokens → TextChunker line-aware fallback
    // Block tipi değişiminde de flush (Paragraph/List/Quote karışmasın).
    private List<ParsedChunk> SplitSection(List<SemanticBlock> section, string headerPath)
    {
        var chunks = new List<ParsedChunk>();
        var pending = new List<SemanticBlock>();
        var pendingTokens = 0;

        void FlushPending()
        {
            if (pending.Count > 0)
            {
                chunks.Add(BuildChunk(pending, headerPath));
                pending.Clear();
                pendingTokens = 0;
            }
        }

        foreach (var block in section)
        {
            var blockTokens = _tokens.Count(_renderer.ToCleanText(block));

            // ATOMIC: Table ve Code asla ortadan bölünmez
            if (block.Type == BlockType.Table || block.Type == BlockType.Code)
            {
                FlushPending();

                if (blockTokens <= _maxTokensPerChunk)
                {
                    chunks.Add(BuildChunk(new List<SemanticBlock> { block }, headerPath));
                }
                else
                {
                    // Aşırı büyük atomic block — son çare TextChunker (line-aware)
                    chunks.AddRange(SplitOversizedBlockWithTextChunker(block, headerPath));
                }
                continue;
            }

            // Tek block bile maxTokens'ı aşıyor → TextChunker fallback
            if (blockTokens > _maxTokensPerChunk)
            {
                FlushPending();
                chunks.AddRange(SplitOversizedBlockWithTextChunker(block, headerPath));
                continue;
            }

            // TIP DEĞIŞIM FLUSH — semantic purity
            // (Paragraph + List + Quote farklı semantik birimler, karışmasın)
            if (pending.Count > 0 && pending[^1].Type != block.Type)
            {
                FlushPending();
            }

            // BÜTÇE TAŞMA FLUSH
            if (pendingTokens + blockTokens > _maxTokensPerChunk && pending.Count > 0)
            {
                FlushPending();
            }

            pending.Add(block);
            pendingTokens += blockTokens;
        }

        FlushPending();
        return chunks;
    }

    // Tek block maxTokens'ı aşıyorsa Microsoft TextChunker ile line-aware splitting.
    // TextChunker markdown-aware: tablo satırı, liste maddesi, code line bütün kalır,
    // cümle ortasından kesmez. Son çare fallback.
    private List<ParsedChunk> SplitOversizedBlockWithTextChunker(SemanticBlock block, string headerPath)
    {
        var rawMd = _renderer.Render(block);

        var lines = TextChunker.SplitMarkDownLines(rawMd, _maxTokensPerChunk, CountTokens);
        var paragraphs = TextChunker.SplitMarkdownParagraphs(
            lines,
            maxTokensPerParagraph: _maxTokensPerChunk,
            overlapTokens: 0,
            chunkHeader: null,
            tokenCounter: CountTokens);

        var chunks = new List<ParsedChunk>();
        foreach (var piece in paragraphs)
        {
            chunks.Add(BuildChunkFromRawMarkdown(piece, block.PageNumber, headerPath));
        }
        return chunks;
    }

    // SemanticBlock listesinden ParsedChunk üretir: render → renumber → clean → header prepend.
    private ParsedChunk BuildChunk(List<SemanticBlock> blocks, string headerPath)
    {
        var rawMd = _renderer.Render(blocks);
        return BuildChunkFromRawMarkdown(rawMd, blocks[0].PageNumber, headerPath);
    }

    // Ham markdown'dan ParsedChunk üretir (oversized block TextChunker output'u için).
    private ParsedChunk BuildChunkFromRawMarkdown(string rawMd, int pageNumber, string headerPath)
    {
        var (content, paths) = RenumberImageMarkers(rawMd.Trim());

        // Header chain'i chunk başına prepend — LLM standalone context için
        if (!string.IsNullOrWhiteSpace(headerPath))
        {
            content = $"**{headerPath}**\n\n{content}";
        }

        // CleanContent: Markdig AST text extraction (regex yok)
        var clean = MakeCleanTextFromAst(content);

        var imagePathJson = paths.Count > 0
            ? System.Text.Json.JsonSerializer.Serialize(paths)
            : null;

        return new ParsedChunk(
            Content: content,
            ImagePath: imagePathJson,
            Header: string.IsNullOrWhiteSpace(headerPath) ? null : headerPath,
            CleanContent: clean,
            PageNumber: pageNumber > 0 ? pageNumber : null);
    }

    // [IMG_PATH:abc] markerlarını [IMG:N] formuna çevirir (chunk-yerel, path dedup).
    // Bizim teknik marker'ımız — string match (regex değil).
    private static (string Text, List<string> Paths) RenumberImageMarkers(string text)
    {
        if (string.IsNullOrEmpty(text)) return (text, new List<string>());

        var paths = new List<string>();
        var idxByPath = new Dictionary<string, int>();
        var sb = new StringBuilder(text.Length);

        var i = 0;
        while (i < text.Length)
        {
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

    // Markdig AST üzerinden düz text çıkarımı (embedding + tam metin araması için).
    // Regex YOK — AST node tipleri kontrolü ile içerik toplanır.
    private static string MakeCleanTextFromAst(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return string.Empty;

        var doc = Markdown.Parse(markdown, MarkdigPipeline);
        var sb = new StringBuilder();
        CollectBlockText(doc, sb);

        return NormalizeWhitespace(StripImgMarkers(sb.ToString()));
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
                            if (cell is Markdig.Extensions.Tables.TableCell tc)
                                CollectBlockText(tc, sb);
                    }
                }
                break;
            case FencedCodeBlock fenced:
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
                if (close > i) { sb.Append(' '); i = close + 1; continue; }
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

    private int CountTokens(string text) => _tokens.Count(text ?? string.Empty);
}
