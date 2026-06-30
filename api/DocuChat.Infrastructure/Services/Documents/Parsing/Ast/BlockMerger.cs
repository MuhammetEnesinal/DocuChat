using DocuChat.Infrastructure.Services.Documents.Parsing.Models;

namespace DocuChat.Infrastructure.Services.Documents.Parsing.Ast;

public sealed class BlockMerger
{
    public List<SemanticBlock> Merge(IReadOnlyList<SemanticBlock> blocks)
    {
        if (blocks.Count < 2) return blocks.ToList();

        var firstIdxOfPage = new Dictionary<int, int>();
        var lastIdxOfPage = new Dictionary<int, int>();
        for (var i = 0; i < blocks.Count; i++)
        {
            var p = blocks[i].PageNumber;
            if (!firstIdxOfPage.ContainsKey(p)) firstIdxOfPage[p] = i;
            lastIdxOfPage[p] = i;
        }

        var result = new List<SemanticBlock>(blocks);
        var i2 = 0;
        while (i2 < result.Count - 1)
        {
            var current = result[i2];
            var next = result[i2 + 1];

            var isBoundary = current.PageNumber != next.PageNumber
                          && lastIdxOfPage.GetValueOrDefault(current.PageNumber, -1) == GetOriginalIdx(blocks, current)
                          && firstIdxOfPage.GetValueOrDefault(next.PageNumber, -1) == GetOriginalIdx(blocks, next);

            if (isBoundary
                && current.Type == BlockType.Table
                && next.Type == BlockType.Table
                && TryMergeTable(current, next, out var merged))
            {
                result[i2] = merged;
                result.RemoveAt(i2 + 1);
                continue;
            }
            i2++;
        }

        return result;
    }

    private static int GetOriginalIdx(IReadOnlyList<SemanticBlock> originals, SemanticBlock b)
    {
        for (var i = 0; i < originals.Count; i++)
            if (ReferenceEquals(originals[i], b) || originals[i].Index == b.Index) return i;
        return -1;
    }

    // Sayfa sınırında ardışık iki tablo → ortak devam mantığıyla birleştir (TableMergeHelper).
    // Tekrar başlık, tam/kısmi "colN" auto-header ve sahte-başlık-veri durumları kayıpsız ele alınır.
    private static bool TryMergeTable(SemanticBlock a, SemanticBlock b, out SemanticBlock merged)
    {
        merged = null!;
        if (a.Table is null || b.Table is null) return false;
        if (!TableMergeHelper.IsContinuation(a.Table, b.Table)) return false;

        var mergedTable = TableMergeHelper.Merge(a.Table, b.Table);
        merged = new SemanticBlock(a.Index, a.PageNumber, BlockType.Table, a.Headers)
        {
            Table = mergedTable,
        };
        merged.Images.AddRange(a.Images);
        merged.Images.AddRange(b.Images);
        return true;
    }
}

