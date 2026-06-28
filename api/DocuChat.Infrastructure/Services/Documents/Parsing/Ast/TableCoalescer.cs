using DocuChat.Infrastructure.Services.Documents.Parsing.Models;

namespace DocuChat.Infrastructure.Services.Documents.Parsing.Ast;

/// <summary>
/// Mistral OCR'ın sayfa içinde fragmana böldüğü tabloları MANTIKSAL bütüne dönüştürür.
/// Aynı sayfa + ardışık + aynı kolon yapısı koşullarını sağlayan tabloları birleştirir.
///
/// BlockMerger sadece sayfa SINIRI birleşimi yapar; bu sınıf sayfa İÇİ fragmanları toparlar.
/// Bilgi kaybı: yok. Satırlar + görseller hepsi korunur, sadece tekrarlanan header satırı düşer.
/// </summary>
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

            // Aynı sayfa + ardışık + aynı kolon yapısı → birleştir.
            // HeaderChain (semantik bölüm) farklıysa birleştirme — chunker zaten ayıracak.
            var j = i + 1;
            var merged = current;
            while (j < blocks.Count
                && blocks[j].PageNumber == current.PageNumber
                && blocks[j].Type == BlockType.Table
                && blocks[j].Table is not null
                && SameHeaderChain(merged.Headers, blocks[j].Headers)
                && CanCoalesce(merged.Table!, blocks[j].Table!))
            {
                merged = AppendTable(merged, blocks[j]);
                j++;
            }

            result.Add(merged);
            i = j;
        }

        return result;
    }

    // İki tablo birleşebilir mi?
    // Koşul: kolon sayısı aynı VE (header'lar aynı VEYA 2. tablonun header'ı Markdig
    // tarafından otomatik üretilmiş "col1, col2..." kalıbı — bu Markdig'in header satırı
    // bulamadığında ürettiği placeholder, yani 2. tablo data-only continuation).
    private static bool CanCoalesce(StructuredTable a, StructuredTable b)
    {
        if (a.Headers.Count != b.Headers.Count) return false;
        if (a.Headers.Count == 0) return false;

        // Auto-header pattern: Markdig "col1, col2, col3..." üretmişse 2. tablo continuation
        var bAutoHeaders = true;
        for (var idx = 0; idx < b.Headers.Count; idx++)
        {
            if (b.Headers[idx] != $"col{idx + 1}") { bAutoHeaders = false; break; }
        }
        if (bAutoHeaders) return true;

        // Aynı header satırı (sayfa içi tablonun başlığı tekrarlanmış)
        for (var idx = 0; idx < a.Headers.Count; idx++)
        {
            if (!string.Equals(a.Headers[idx], b.Headers[idx], StringComparison.OrdinalIgnoreCase))
                return false;
        }
        return true;
    }

    // HeaderChain karşılaştırma — iki block aynı semantik bölümün altında mı?
    // Path eşitliği yeterli (level + text aynı sıra).
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

    // İki tabloyu birleştir: rows concat + images concat.
    // RawMarkdown = null çünkü yeni kombine yapı orijinal markdown'a karşılık gelmiyor;
    // renderer fallback ile yeniden basacak (block.Type == Table → RenderTable).
    private static SemanticBlock AppendTable(SemanticBlock first, SemanticBlock next)
    {
        var rows = first.Table!.Rows.Concat(next.Table!.Rows).ToList();
        var mergedTable = new StructuredTable(first.Table.Headers, rows);

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
