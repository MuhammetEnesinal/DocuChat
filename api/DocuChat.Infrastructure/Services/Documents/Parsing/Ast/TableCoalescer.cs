using DocuChat.Infrastructure.Services.Documents.Parsing.Models;

namespace DocuChat.Infrastructure.Services.Documents.Parsing.Ast;

// Mistral OCR'ın AYNI SAYFA içinde fragmana böldüğü tabloları mantıksal bütüne dönüştürür.
// Birleştirme kuralları TableMergeHelper'da (sayfa-sınırı BlockMerger ile ortak): tekrar başlık,
// tam/kısmi "colN" auto-header ve sahte-başlık-veri durumlarının hepsi kayıpsız ele alınır.
// BlockMerger sayfa SINIRI birleşimi yapar; bu sınıf sayfa İÇİ fragmanları toparlar.
public sealed class TableCoalescer
{
    public List<SemanticBlock> Coalesce(IReadOnlyList<SemanticBlock> blocks)
    {
        if (blocks.Count < 2) return blocks.ToList();

        var result = new List<SemanticBlock>();
        var i = 0;
        while (i < blocks.Count)
        {
            var current = blocks[i];

            if (current.Type != BlockType.Table || current.Table is null)
            {
                result.Add(current);
                i++;
                continue;
            }

            // Ardışık devam parçalarını (aynı sayfa + aynı bölüm + devam yapısı) tek tabloda topla.
            var j = i + 1;
            var merged = current;
            while (j < blocks.Count
                && blocks[j].PageNumber == current.PageNumber
                && blocks[j].Type == BlockType.Table
                && blocks[j].Table is not null
                && SameHeaderChain(merged.Headers, blocks[j].Headers)
                && TableMergeHelper.IsContinuation(merged.Table!, blocks[j].Table!))
            {
                merged = AppendTable(merged, blocks[j]);
                j++;
            }

            result.Add(merged);
            i = j;
        }

        return result;
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

    // İki tabloyu birleştir (ortak mantık) + görselleri koru. RawMarkdown = null → renderer yeniden basar.
    private static SemanticBlock AppendTable(SemanticBlock first, SemanticBlock next)
    {
        var mergedTable = TableMergeHelper.Merge(first.Table!, next.Table!);
        var merged = new SemanticBlock(first.Index, first.PageNumber, BlockType.Table, first.Headers)
        {
            Table = mergedTable,
            RawMarkdown = null,
            TextContent = string.Empty
        };
        merged.Images.AddRange(first.Images);
        merged.Images.AddRange(next.Images);
        return merged;
    }
}
