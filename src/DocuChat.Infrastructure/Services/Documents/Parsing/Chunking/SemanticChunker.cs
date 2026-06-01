using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DocuChat.Application.Interfaces.Services;
using DocuChat.Infrastructure.Services.Documents.Parsing.Models;
using DocuChat.Infrastructure.Services.Documents.Parsing.Rendering;

namespace DocuChat.Infrastructure.Services.Documents.Parsing.Chunking;

public sealed class SemanticChunker
{
    private readonly ITokenCounter _tokens;
    private readonly IMarkdownRenderer _renderer;
    private readonly SemanticSplitter _splitter;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = false };
    private static readonly Regex ImgPathRegex = new(@"\[IMG_PATH:([^\]]+)\]", RegexOptions.Compiled);

    public SemanticChunker(ITokenCounter tokens, IMarkdownRenderer renderer, SemanticSplitter splitter)
    {
        _tokens = tokens;
        _renderer = renderer;
        _splitter = splitter;
    }

    public async Task<List<PipelineChunk>> ChunkAsync(
        IReadOnlyList<SemanticBlock> blocks,
        int maxTokens = 800,
        CancellationToken ct = default)
    {
        var chunks = new List<PipelineChunk>();
        if (blocks.Count == 0) return chunks;

        var groups = GroupByHeader(blocks);
        foreach (var group in groups)
            await FlushGroupAsync(group, group[0].Headers.ToPath(), maxTokens, chunks, ct);

        return chunks;
    }

    private static List<List<SemanticBlock>> GroupByHeader(IReadOnlyList<SemanticBlock> blocks)
    {
        var groups = new List<List<SemanticBlock>>();
        var current = new List<SemanticBlock> { blocks[0] };
        var currentPath = blocks[0].Headers.ToPath();

        for (var i = 1; i < blocks.Count; i++)
        {
            var path = blocks[i].Headers.ToPath();
            if (path == currentPath) current.Add(blocks[i]);
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

    private async Task FlushGroupAsync(
        List<SemanticBlock> group, string header, int maxTokens,
        List<PipelineChunk> output, CancellationToken ct)
    {
        var pending = new List<SemanticBlock>();
        var pendingTokens = 0;

        foreach (var block in group)
        {
            // Table → atomik
            if (block.Type == BlockType.Table)
            {
                if (pending.Count > 0)
                {
                    output.Add(BuildChunk(pending, header));
                    pending.Clear();
                    pendingTokens = 0;
                }
                output.Add(BuildChunk(new List<SemanticBlock> { block }, header));
                continue;
            }

            var blockTokens = _tokens.Count(_renderer.ToCleanText(block));

            // Tek başına bütçeyi aşıyor → embedding-based semantic split
            if (blockTokens > maxTokens)
            {
                if (pending.Count > 0)
                {
                    output.Add(BuildChunk(pending, header));
                    pending.Clear();
                    pendingTokens = 0;
                }
                await SplitOversizedBlockAsync(block, header, maxTokens, output, ct);
                continue;
            }

            if (pendingTokens + blockTokens > maxTokens)
            {
                output.Add(BuildChunk(pending, header));
                pending.Clear();
                pendingTokens = 0;
            }

            pending.Add(block);
            pendingTokens += blockTokens;
        }

        if (pending.Count > 0)
            output.Add(BuildChunk(pending, header));
    }

    private async Task SplitOversizedBlockAsync(
        SemanticBlock block, string header, int maxTokens,
        List<PipelineChunk> output, CancellationToken ct)
    {
        // List block için item-bazlı split (yapısal, dil yok)
        if (block.Type == BlockType.List && block.ListItems.Count > 1)
        {
            var pending = new List<string>();
            var pendingTokens = 0;
            foreach (var item in block.ListItems)
            {
                var t = _tokens.Count(item);
                if (pendingTokens + t > maxTokens && pending.Count > 0)
                {
                    output.Add(BuildChunk(new List<SemanticBlock> { MakeListPiece(block, pending) }, header));
                    pending.Clear(); pendingTokens = 0;
                }
                pending.Add(item); pendingTokens += t;
            }
            if (pending.Count > 0)
                output.Add(BuildChunk(new List<SemanticBlock> { MakeListPiece(block, pending) }, header));
            return;
        }

        // Text block → embedding semantic split
        var pieces = await _splitter.SplitAsync(block.TextContent, maxTokens, ct);
        for (var i = 0; i < pieces.Count; i++)
        {
            var piece = new SemanticBlock(block.Index, block.PageNumber, block.Type, block.Headers)
            {
                TextContent = pieces[i]
            };
            // Image'ları sadece son parçaya iliştir (basitlik)
            if (i == pieces.Count - 1) piece.Images.AddRange(block.Images);
            output.Add(BuildChunk(new List<SemanticBlock> { piece }, header));
        }
    }

    private static SemanticBlock MakeListPiece(SemanticBlock original, List<string> items)
    {
        var b = new SemanticBlock(original.Index, original.PageNumber, BlockType.List, original.Headers)
        {
            IsOrdered = original.IsOrdered
        };
        b.ListItems.AddRange(items);
        return b;
    }

    private PipelineChunk BuildChunk(List<SemanticBlock> blocks, string header)
    {
        // 1) Markdown — renderer
        var rawMd = _renderer.Render(blocks);

        // 2) [IMG_PATH:...] → [IMG:N] + path listesi
        var (md, imagePaths) = RenumberImageMarkers(rawMd);

        // 3) CleanContent — marker'sız
        var cleanSb = new StringBuilder();
        for (var i = 0; i < blocks.Count; i++)
        {
            if (i > 0) cleanSb.Append(' ');
            cleanSb.Append(_renderer.ToCleanText(blocks[i]));
        }
        var cleanRaw = ImgPathRegex.Replace(cleanSb.ToString(), " ");
        var clean = NormalizeWhitespace(cleanRaw);

        // 4) Atomik tablo → StructuredTableJson
        string? tableJson = null;
        if (blocks.Count == 1 && blocks[0].Table != null)
        {
            tableJson = JsonSerializer.Serialize(new
            {
                headers = blocks[0].Table!.Headers,
                rows = blocks[0].Table!.Rows
            }, JsonOpts);
        }

        return new PipelineChunk
        {
            MarkdownContent = md,
            CleanContent = clean,
            Header = header,
            PageNumber = blocks[0].PageNumber,
            StructuredTableJson = tableJson,
            TokenCount = _tokens.Count(clean),
            ImagePaths = imagePaths,
        };
    }

    private static (string Text, List<string> Paths) RenumberImageMarkers(string text)
    {
        if (string.IsNullOrEmpty(text)) return (text, new List<string>());

        var paths = new List<string>();
        var idxByPath = new Dictionary<string, int>();
        var result = ImgPathRegex.Replace(text, m =>
        {
            var path = m.Groups[1].Value.Trim();
            if (!idxByPath.TryGetValue(path, out var n))
            {
                paths.Add(path);
                n = paths.Count;
                idxByPath[path] = n;
            }
            return $"[IMG:{n}]";
        });
        return (result, paths);
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
