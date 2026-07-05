using System.Text;
using System.Text.RegularExpressions;
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
using DocuChat.Infrastructure.Services.Documents.Parsing.Models;
using DocuChat.Infrastructure.Services.Documents.Parsing.Rendering;

namespace DocuChat.Infrastructure.Services.Documents.Parsing.Chunking;

public sealed class SemanticChunker
{
    private readonly ITokenCounter _tokens;
    private readonly IMarkdownRenderer _renderer;
    private readonly SemanticSplitter _splitter;
    private readonly int _maxAtomicTokens;
    private static readonly Regex ImgPathRegex = new(@"\[IMG_PATH:([^\]]+)\]", RegexOptions.Compiled);

    public SemanticChunker(
        ITokenCounter tokens,
        IMarkdownRenderer renderer,
        SemanticSplitter splitter,
        int maxAtomicTokens = 1500)
    {
        _tokens = tokens;
        _renderer = renderer;
        _splitter = splitter;
        _maxAtomicTokens = maxAtomicTokens;
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

        // POST-PASS: ardışık aynı-header chunk'ları budget içinde birleştir.
        // IMG markerları explicit renumber edilir (eski MergeSmallAdjacentChunks bug'ı yok).
        // Multi-page fragmanları ("olmak." gibi) da burada birleşir — aynı section'a aitler.
        chunks = MergeSameHeaderChunks(chunks, maxTokens);

        return chunks;
    }

    /// <summary>
    /// Post-pass: ardışık aynı-header chunk'lar budget içinde birleştirilir.
    /// Header chain aynı → semantik aynı bölüm. Tokens ≤ maxTokens → tek chunk olur.
    /// IMG markerları doğru renumber edilir (eski bug'ı önler).
    /// Multi-page (önceki sayfa sonu + sonraki sayfa başı aynı section) da birleşir.
    /// </summary>
    private List<PipelineChunk> MergeSameHeaderChunks(List<PipelineChunk> chunks, int maxTokens)
    {
        if (chunks.Count <= 1) return chunks;

        var result = new List<PipelineChunk>();
        var current = chunks[0];
        var currentTokens = _tokens.Count(current.CleanContent ?? string.Empty);

        for (var i = 1; i < chunks.Count; i++)
        {
            var next = chunks[i];
            var nextTokens = _tokens.Count(next.CleanContent ?? string.Empty);

            var sameHeader = string.Equals(
                current.Header ?? string.Empty,
                next.Header ?? string.Empty,
                StringComparison.Ordinal);

            if (sameHeader && currentTokens + nextTokens <= maxTokens)
            {
                current = MergeTwoChunks(current, next);
                currentTokens = _tokens.Count(current.CleanContent ?? string.Empty);
            }
            else
            {
                result.Add(current);
                current = next;
                currentTokens = nextTokens;
            }
        }
        result.Add(current);
        return result;
    }

    /// <summary>
    /// İki PipelineChunk'ı birleştirir:
    ///   1. b'nin başındaki duplicate header prefix'i kaldırır
    ///   2. b'nin IMG markerlarını a'nın path sayısı kadar offset'le renumber eder
    ///   3. ImagePaths concat
    ///   4. CleanContent + MarkdownContent birleştirir
    /// PageNumber: ilk chunk'ınki (citation için ilk görünüş)
    /// </summary>
    private static PipelineChunk MergeTwoChunks(PipelineChunk a, PipelineChunk b)
    {
        // b'nin MarkdownContent'inin başında **header**\n\n duplicate'ı varsa kaldır
        var bContent = b.MarkdownContent;
        if (!string.IsNullOrWhiteSpace(a.Header))
        {
            var headerPrefix = $"**{a.Header}**\n\n";
            if (bContent.StartsWith(headerPrefix, StringComparison.Ordinal))
                bContent = bContent.Substring(headerPrefix.Length);
        }

        // IMG markerları renumber: a'da N path → b'nin [IMG:M] → [IMG:N+M]
        var offset = a.ImagePaths.Count;
        if (offset > 0 && !string.IsNullOrEmpty(bContent))
        {
            bContent = Regex.Replace(bContent, @"\[IMG:(\d+)([^\]]*)\]", m =>
            {
                if (!int.TryParse(m.Groups[1].Value, out var n)) return m.Value;
                return $"[IMG:{n + offset}{m.Groups[2].Value}]";
            });
        }

        var mergedMd = a.MarkdownContent + "\n\n" + bContent;
        var mergedClean = ((a.CleanContent ?? string.Empty) + " " + (b.CleanContent ?? string.Empty)).Trim();
        var mergedPaths = new List<string>(a.ImagePaths);
        mergedPaths.AddRange(b.ImagePaths);

        return new PipelineChunk
        {
            MarkdownContent = mergedMd,
            CleanContent = mergedClean,
            Header = a.Header,
            PageNumber = a.PageNumber,  // ilk chunk'ın page'i (citation için)
            ImagePaths = mergedPaths,
        };
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
            // Table & Code & Math → atomik (yapı/sözcük dağarcığı korunur).
            // Hard limit: > _maxAtomicTokens (config'den) ise yapısal bölünme uygulanır.
            // Math: LaTeX formülleri ortadan kesilirse syntax bozulur, atomic zorunlu.
            if (block.Type == BlockType.Table || block.Type == BlockType.Code || block.Type == BlockType.Math)
            {
                if (pending.Count > 0)
                {
                    output.Add(BuildChunk(pending, header));
                    pending.Clear();
                    pendingTokens = 0;
                }

                var atomicTokens = _tokens.Count(_renderer.ToCleanText(block));
                if (atomicTokens > _maxAtomicTokens)
                {
                    SplitOversizedAtomic(block, header, output);
                }
                else
                {
                    output.Add(BuildChunk(new List<SemanticBlock> { block }, header));
                }
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

            // TYPE CHANGE FLUSH — semantic purity garantisi.
            // Pending'in son block tipi farklıysa flush et: Paragraph + List + Quote bir
            // chunk içinde karışmasın (vektör saflığı + retrieval kalitesi).
            if (pending.Count > 0 && pending[^1].Type != block.Type)
            {
                output.Add(BuildChunk(pending, header));
                pending.Clear();
                pendingTokens = 0;
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
        // OrderedList split olduğunda orijinal madde numaralandırması korunur (StartIndex).
        if (block.Type == BlockType.List && block.ListItems.Count > 1)
        {
            var pending = new List<string>();
            var pendingTokens = 0;
            var pieceStartIndex = 0;  // Bu parça orijinal listenin kaçıncı item'ından başlıyor
            var processedCount = 0;

            foreach (var item in block.ListItems)
            {
                var t = _tokens.Count(item);
                if (pendingTokens + t > maxTokens && pending.Count > 0)
                {
                    output.Add(BuildChunk(new List<SemanticBlock> { MakeListPiece(block, pending, pieceStartIndex) }, header));
                    pieceStartIndex = processedCount;  // Sonraki parça bu index'ten başlar
                    pending.Clear(); pendingTokens = 0;
                }
                pending.Add(item);
                pendingTokens += t;
                processedCount++;
            }
            if (pending.Count > 0)
                output.Add(BuildChunk(new List<SemanticBlock> { MakeListPiece(block, pending, pieceStartIndex) }, header));
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

    // Dev atomik block bölücüsü — Table/Code için yapısal split (semantic split kullanmaz).
    // Table: row-based, her parça header satırını taşır (LLM context kaybetmez).
    // Code: line-based, semantik üst-yapı yok.
    private void SplitOversizedAtomic(SemanticBlock block, string header, List<PipelineChunk> output)
    {
        if (block.Type == BlockType.Table && block.Table is { Headers.Count: > 0 })
        {
            var headers = block.Table.Headers.ToList();
            var rows = block.Table.Rows.ToList();
            if (rows.Count == 0)
            {
                output.Add(BuildChunk(new List<SemanticBlock> { block }, header));
                return;
            }

            var headerRowApprox = _tokens.Count(string.Join(" ", headers));
            var pending = new List<IReadOnlyDictionary<string, string>>();
            var pendingTokens = headerRowApprox;
            foreach (var row in rows)
            {
                var rowTokens = _tokens.Count(string.Join(" ", row.Values));
                if (pending.Count > 0 && pendingTokens + rowTokens > _maxAtomicTokens)
                {
                    EmitTablePiece(block, header, headers, pending, output);
                    pending.Clear();
                    pendingTokens = headerRowApprox;
                }
                pending.Add(row);
                pendingTokens += rowTokens;
            }
            if (pending.Count > 0)
                EmitTablePiece(block, header, headers, pending, output);
            return;
        }

        if (block.Type == BlockType.Code && !string.IsNullOrEmpty(block.TextContent))
        {
            // Universal recursive split: \n\n (fonksiyon/blok ayrımı) → \n (satır) → " " (token).
            // Sıfır dil-spesifik kural, "obj.method()" gibi punctuation içinden bölmez (. yok).
            // Fonksiyon arasında boş satır varsa otomatik o sınırı yakalar.
            var pieces = RecursiveSplitter.Split(
                block.TextContent, _maxAtomicTokens, _tokens, RecursiveSplitter.CodeSeparators);
            foreach (var piece in pieces)
                EmitCodePiece(block, header, piece, output);
            return;
        }

        // Fallback: tek chunk bırak (BGE-M3 8K context yutar)
        output.Add(BuildChunk(new List<SemanticBlock> { block }, header));
    }

    private void EmitTablePiece(SemanticBlock orig, string header,
        IReadOnlyList<string> headers,
        List<IReadOnlyDictionary<string, string>> rows,
        List<PipelineChunk> output)
    {
        var piece = new SemanticBlock(orig.Index, orig.PageNumber, BlockType.Table, orig.Headers)
        {
            Table = new Models.StructuredTable(headers, rows.ToList())
        };
        output.Add(BuildChunk(new List<SemanticBlock> { piece }, header));
    }

    private void EmitCodePiece(SemanticBlock orig, string header,
        string text, List<PipelineChunk> output)
    {
        var piece = new SemanticBlock(orig.Index, orig.PageNumber, BlockType.Code, orig.Headers)
        {
            TextContent = text
        };
        output.Add(BuildChunk(new List<SemanticBlock> { piece }, header));
    }

    private static SemanticBlock MakeListPiece(SemanticBlock original, List<string> items, int startIndex = 0)
    {
        var b = new SemanticBlock(original.Index, original.PageNumber, BlockType.List, original.Headers)
        {
            IsOrdered = original.IsOrdered,
            StartIndex = startIndex
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

        // ⭐ Header'ı Content'in başına prepend et — Mistral OCR heading olarak algıladığında
        // başlık metni Content'te DE görünsün (sadece metadata'da değil).
        // Hem markdown (LLM için bold) hem clean (embedding/BM25 için) versiyonu güncellenir.
        if (!string.IsNullOrWhiteSpace(header))
        {
            md = $"**{header}**\n\n{md}";
            clean = $"{header} {clean}";
        }

        return new PipelineChunk
        {
            MarkdownContent = md,
            CleanContent = clean,
            Header = header,
            PageNumber = blocks[0].PageNumber,
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
