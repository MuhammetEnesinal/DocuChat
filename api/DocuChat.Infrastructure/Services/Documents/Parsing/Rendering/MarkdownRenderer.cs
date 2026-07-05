using System.Text;
using DocuChat.Infrastructure.Services.Documents.Parsing.Models;

namespace DocuChat.Infrastructure.Services.Documents.Parsing.Rendering;

public sealed class MarkdownRenderer : IMarkdownRenderer
{
    public string Render(SemanticBlock block)
    {
        // ÖZEL DURUM: Tablo + block.Images var → structured RenderTable kullan ki görseller
        // boş hücrelere yerleştirilebilsin. RawMarkdown bypass edilir.
        // Tabloda görsel YOKSA RawMarkdown yolu kullanılır (Mistral'in orijinal formatı korunur).
        if (block.Type == BlockType.Table && block.Images.Count > 0 && block.Table is not null)
            return RenderTable(block);

        // RawMarkdown varsa → birebir orijinal markdown (nested list, bold, italic, link
        // — Mistral OCR'ın verdiği yapı %100 korunur). Sadece image marker'ları append edilir.
        // RawMarkdown YOKSA (sentetik split piece'leri) → eski re-render fallback.
        if (!string.IsNullOrEmpty(block.RawMarkdown))
        {
            var sb = new StringBuilder(block.RawMarkdown);
            AppendImageMarkers(sb, block.Images);
            return sb.ToString().TrimEnd();
        }

        return block.Type switch
        {
            BlockType.Paragraph       => RenderTextBlock(block),
            BlockType.Quote           => RenderQuote(block),
            BlockType.Code            => RenderCode(block),
            BlockType.List            => RenderList(block),
            BlockType.Table           => RenderTable(block),
            BlockType.Math            => RenderMath(block),
            BlockType.Definition      => RenderTextBlock(block),
            BlockType.Figure          => RenderTextBlock(block),
            BlockType.Alert           => RenderQuote(block),         // alert quote-like syntax
            BlockType.Footnote        => RenderTextBlock(block),
            BlockType.YamlFrontMatter => RenderTextBlock(block),
            BlockType.ThematicBreak   => RenderThematicBreak(block),
            _                         => RenderTextBlock(block)
        };
    }

    private static string RenderMath(SemanticBlock block)
    {
        var sb = new StringBuilder();
        sb.AppendLine("$$");
        sb.AppendLine(block.TextContent);
        sb.Append("$$");
        if (block.Images.Count > 0)
        {
            sb.AppendLine();
            AppendImageMarkers(sb, block.Images);
        }
        return sb.ToString();
    }

    private static string RenderThematicBreak(SemanticBlock block)
    {
        var sb = new StringBuilder("---");
        AppendImageMarkers(sb, block.Images);
        return sb.ToString();
    }

    public string Render(IEnumerable<SemanticBlock> blocks)
    {
        var sb = new StringBuilder();
        var first = true;
        foreach (var b in blocks)
        {
            if (!first) sb.Append("\n\n");
            sb.Append(Render(b));
            first = false;
        }
        return sb.ToString();
    }

    public string ToCleanText(SemanticBlock block) => block.Type switch
    {
        BlockType.Table          => TableToCleanText(block.Table),
        BlockType.List           => string.Join(" ", block.ListItems),
        BlockType.ThematicBreak  => string.Empty,  // visual separator, embedding'e girmesin
        _                        => block.TextContent
    };

    // Block tipi başına renderer

    private static string RenderTextBlock(SemanticBlock block)
    {
        var sb = new StringBuilder(block.TextContent);
        AppendImageMarkers(sb, block.Images);
        return sb.ToString();
    }

    private static string RenderQuote(SemanticBlock block)
    {
        var sb = new StringBuilder();
        foreach (var line in block.TextContent.Split('\n'))
            sb.Append("> ").AppendLine(line);
        AppendImageMarkers(sb, block.Images);
        return sb.ToString().TrimEnd();
    }

    private static string RenderCode(SemanticBlock block)
    {
        var sb = new StringBuilder();
        sb.AppendLine("```");
        sb.AppendLine(block.TextContent);
        sb.Append("```");
        // Image marker'ı code fence'in dışına ekle
        if (block.Images.Count > 0)
        {
            sb.AppendLine();
            AppendImageMarkers(sb, block.Images);
        }
        return sb.ToString();
    }

    private static string RenderList(SemanticBlock block)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < block.ListItems.Count; i++)
        {
            // StartIndex split olmuş listelerin orijinal numarasını korur:
            // örn. madde 7-14 parçası StartIndex=6 → "7.", "8."...
            var prefix = block.IsOrdered ? $"{block.StartIndex + i + 1}. " : "- ";
            sb.Append(prefix).AppendLine(block.ListItems[i]);
        }
        AppendImageMarkers(sb, block.Images);
        return sb.ToString().TrimEnd();
    }

    private static string RenderTable(SemanticBlock block)
    {
        var table = block.Table;
        if (table == null || table.Headers.Count == 0) return string.Empty;

        var sb = new StringBuilder();

        // GÖRSEL KOLONU TESPİTİ — yapısal (manuel kelime listesi yok).
        // En fazla boş hücreli kolonu seç (≥%50 boş olmalı).
        // Sub-header satırlarını (≥%50 hücresi boş) skip ederek images'ı data row'larına yerleştir.
        var imageColIdx = DetectImageColumn(table);

        // Resimler boş image-hücrelerine Y-sırasında sırayla yerleştirilir. Sayfa sınırında
        // bölünen tablolarda resim ve boş hücre sayıları eşleşmeyebilir; bu yüzden tam eşitlik
        // aranmaz. Resim sayısı boş hücreden azsa kalan hücreler boş kalır, fazlaysa artan
        // resimler tablo altına eklenir (aşağıdaki remaining bloğu).
        var dataRowEmptyCount = 0;
        if (imageColIdx >= 0)
        {
            foreach (var row in table.Rows)
            {
                if (IsLikelySubHeaderRow(row, table.Headers)) continue;
                var imgColName = table.Headers[imageColIdx];
                var cellAtImgCol = row.TryGetValue(imgColName, out var v) ? v ?? string.Empty : string.Empty;
                if (string.IsNullOrWhiteSpace(cellAtImgCol)) dataRowEmptyCount++;
            }
        }

        var canPlaceInCells = imageColIdx >= 0 && block.Images.Count > 0 && dataRowEmptyCount > 0;

        // Header — "colN" pozisyonel placeholder'ları (TableExtractor'ın boş başlık dolgusu)
        // kullanıcıya/LLM'e SIZMASIN: markdown'da boş hücre olarak yazılır.
        sb.Append('|');
        for (var hIdx = 0; hIdx < table.Headers.Count; hIdx++)
        {
            var h = IsColPlaceholder(table.Headers[hIdx], hIdx) ? string.Empty : table.Headers[hIdx];
            sb.Append(' ').Append(EscapeCell(h)).Append(" |");
        }
        sb.AppendLine();

        // Separator
        sb.Append('|');
        foreach (var _ in table.Headers) sb.Append(" --- |");
        sb.AppendLine();

        // Data — sub-header row'larını skip ederek image column'a görsel yerleştir
        var imgIdx = 0;
        foreach (var row in table.Rows)
        {
            // Sub-header tespiti: bu row'da hücrelerin yarısından fazlası boşsa muhtemelen
            // section/category başlığı satırı, image atanmaz (sadece data row'lar görsel alır).
            var isSubHeaderRow = IsLikelySubHeaderRow(row, table.Headers);

            sb.Append('|');
            for (var c = 0; c < table.Headers.Count; c++)
            {
                var colName = table.Headers[c];
                var cell = row.TryGetValue(colName, out var v) ? v ?? string.Empty : string.Empty;

                // Image column + DATA row + boş hücre + atayacak görsel → IMG_PATH yerleştir
                if (canPlaceInCells
                    && !isSubHeaderRow
                    && c == imageColIdx
                    && string.IsNullOrWhiteSpace(cell)
                    && imgIdx < block.Images.Count)
                {
                    cell = $"[IMG_PATH:{block.Images[imgIdx].Path}]";
                    imgIdx++;
                }

                sb.Append(' ').Append(EscapeCell(cell)).Append(" |");
            }
            sb.AppendLine();
        }

        // Hücreye yerleşmeyen görseller (image count > data row count veya image column yok) →
        // tablo altına append (eski davranış).
        if (imgIdx < block.Images.Count)
        {
            var remaining = new List<ImageWithBbox>();
            for (var k = imgIdx; k < block.Images.Count; k++) remaining.Add(block.Images[k]);
            AppendImageMarkers(sb, remaining);
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// Yapısal görsel kolonu tespiti — manuel kelime listesi yok, sadece veri kontrolü.
    /// EN ÇOK boş hücreli kolon seçilir (≥%50 boş olmalı). Bu sayede sub-header satırlarda
    /// kolon yazısı ("Resim") olsa bile, data row'ların boş hücreleri kolonu image column yapar.
    /// </summary>
    private static int DetectImageColumn(StructuredTable table)
    {
        if (table.Rows.Count == 0) return -1;

        var bestCol = -1;
        var maxEmpty = 0;

        for (var c = 0; c < table.Headers.Count; c++)
        {
            var colName = table.Headers[c];
            var emptyCount = 0;
            foreach (var row in table.Rows)
            {
                var cell = row.TryGetValue(colName, out var v) ? v ?? string.Empty : string.Empty;
                if (string.IsNullOrWhiteSpace(cell)) emptyCount++;
            }
            // ≥%50 boş + öncekilerden daha fazla boş
            if (emptyCount * 2 >= table.Rows.Count && emptyCount > maxEmpty)
            {
                maxEmpty = emptyCount;
                bestCol = c;
            }
        }
        return bestCol;
    }

    /// <summary>
    /// Sub-header (section/category) satır tespiti — yapısal, manuel kural yok.
    /// Bir row'da hücrelerin ≥%50'si boşsa muhtemelen başlık satırıdır (örn. "Kullanılan El Aletleri")
    /// veya kolon-label satırıdır. Bu satırlara görsel yerleştirilmez (sadece data row'lar).
    /// </summary>
    private static bool IsLikelySubHeaderRow(IReadOnlyDictionary<string, string> row, IReadOnlyList<string> headers)
    {
        if (headers.Count == 0) return true;
        var emptyCount = 0;
        foreach (var h in headers)
        {
            var cell = row.TryGetValue(h, out var v) ? v ?? string.Empty : string.Empty;
            if (string.IsNullOrWhiteSpace(cell)) emptyCount++;
        }
        return emptyCount * 2 >= headers.Count;
    }

    private static void AppendImageMarkers(StringBuilder sb, IReadOnlyList<ImageWithBbox> images)
    {
        if (images.Count == 0) return;
        if (sb.Length > 0 && sb[^1] != '\n') sb.AppendLine();
        foreach (var img in images)
            sb.Append("[IMG_PATH:").Append(img.Path).Append(']').AppendLine();
    }

    private static string EscapeCell(string s) =>
        string.IsNullOrEmpty(s) ? string.Empty : s.Replace("|", "\\|").Replace("\n", " ").Trim();

    // "colN" pozisyonel placeholder mı? (TableExtractor boş başlık hücresine col{i+1} basar.)
    private static bool IsColPlaceholder(string header, int index) =>
        string.Equals(header, $"col{index + 1}", StringComparison.Ordinal);

    private static string TableToCleanText(StructuredTable? table)
    {
        if (table == null) return string.Empty;
        var sb = new StringBuilder();
        // colN placeholder'ları embedding metnine de girmesin (anlamsız token)
        var realHeaders = table.Headers.Where((h, i) => !IsColPlaceholder(h, i));
        sb.Append(string.Join(" ", realHeaders));
        foreach (var row in table.Rows)
        {
            sb.Append(' ');
            sb.Append(string.Join(" ", row.Values));
        }
        return sb.ToString().Trim();
    }
}
