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

namespace DocuChat.Infrastructure.Services.Documents.Parsing.Ast;

// Aynı semantik bölüm + aynı tip + ardışık MİNİ block'ları (token < minTokens) birleştirir.
// Amaç: chunker'a girmeden önce parçalı küçük yapıları mantıksal bütüne toparlamak.
// Bilgi kaybı: yok. Tüm TextContent + RawMarkdown + Images korunur, sadece concat edilir.
// Kurallar:
// - Aynı HeaderChain (semantik bölüm sınırına saygı)
// - Aynı BlockType (Paragraph + List karışmaz — semantic purity)
// - Tek tek mini olmalı (ikisi de minTokens altında)
// - Birleşik token sayısı maxMergedTokens'ı aşmamalı (yeniden büyük block doğurma)
// - Quote, Table, Code BU SINIFTA HARİÇ — semantik birim olarak ayrı tutulur.
public sealed class BlockCoalescer
{
    private readonly ITokenCounter _tokens;
    private readonly int _minTokens;
    private readonly int _maxMergedTokens;

    public BlockCoalescer(ITokenCounter tokens, int minTokens = 80, int maxMergedTokens = 400)
    {
        _tokens = tokens;
        _minTokens = minTokens;
        _maxMergedTokens = maxMergedTokens;
    }

    public List<SemanticBlock> Coalesce(IReadOnlyList<SemanticBlock> blocks)
    {
        if (blocks.Count < 2) return blocks.ToList();

        var result = new List<SemanticBlock>();
        var i = 0;
        while (i < blocks.Count)
        {
            var current = blocks[i];

            // Coalesce SADECE Paragraph veya List tipinde — Quote/Table/Code semantik bütün
            if (current.Type != BlockType.Paragraph && current.Type != BlockType.List)
            {
                result.Add(current);
                i++;
                continue;
            }

            var currentTokens = _tokens.Count(current.TextContent);
            if (currentTokens >= _minTokens)
            {
                result.Add(current);
                i++;
                continue;
            }

            // Ardışık mini block'ları topla — aynı type + aynı header + birleşik token bütçesi içinde.
            // Sayfa fark etmez: sayfa sonu kesilen mini paragraf/liste fragmanları (örn. "olmak."
            // kelimesi sonraki sayfada tek başına chunk olur) birleştirilir.
            // Merged block'un PageNumber'ı ilk block'unkidir (citation'da ilk görünüş yeri).
            var merged = current;
            var mergedTokens = currentTokens;
            var j = i + 1;
            while (j < blocks.Count
                && blocks[j].Type == current.Type
                && SameHeaderChain(blocks[j].Headers, current.Headers))
            {
                var nextTokens = _tokens.Count(blocks[j].TextContent);
                // Diğer block da mini OLMALI (büyük bir block'u küçük olana yapıştırmayalım)
                if (nextTokens >= _minTokens) break;
                if (mergedTokens + nextTokens > _maxMergedTokens) break;

                merged = MergeBlocks(merged, blocks[j]);
                mergedTokens += nextTokens;
                j++;
            }

            result.Add(merged);
            i = j;
        }

        return result;
    }

    private static SemanticBlock MergeBlocks(SemanticBlock a, SemanticBlock b)
    {
        var combinedText = string.IsNullOrEmpty(a.TextContent)
            ? b.TextContent
            : (string.IsNullOrEmpty(b.TextContent) ? a.TextContent : a.TextContent + " " + b.TextContent);

        var combinedRaw = string.IsNullOrEmpty(a.RawMarkdown)
            ? b.RawMarkdown
            : (string.IsNullOrEmpty(b.RawMarkdown) ? a.RawMarkdown : a.RawMarkdown + "\n\n" + b.RawMarkdown);

        var merged = new SemanticBlock(a.Index, a.PageNumber, a.Type, a.Headers)
        {
            TextContent = combinedText,
            RawMarkdown = combinedRaw,
            IsOrdered = a.IsOrdered,
        };

        // List items birleştirilir (List type için)
        if (a.Type == BlockType.List)
        {
            merged.ListItems.AddRange(a.ListItems);
            merged.ListItems.AddRange(b.ListItems);
        }

        merged.Images.AddRange(a.Images);
        merged.Images.AddRange(b.Images);
        return merged;
    }

    private static bool SameHeaderChain(HeaderChain a, HeaderChain b)
    {
        if (a.Items.Count != b.Items.Count) return false;
        for (var idx = 0; idx < a.Items.Count; idx++)
        {
            if (a.Items[idx].Level != b.Items[idx].Level) return false;
            if (!string.Equals(a.Items[idx].Text, b.Items[idx].Text, StringComparison.Ordinal))
                return false;
        }
        return true;
    }
}
