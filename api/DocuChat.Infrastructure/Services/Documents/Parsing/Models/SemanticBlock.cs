namespace DocuChat.Infrastructure.Services.Documents.Parsing.Models;

public sealed class SemanticBlock
{
    public SemanticBlock(int index, int pageNumber, BlockType type, HeaderChain headers)
    {
        Index = index;
        PageNumber = pageNumber;
        Type = type;
        Headers = headers;
    }

    public int Index { get; }
    public int PageNumber { get; }
    public BlockType Type { get; }
    public HeaderChain Headers { get; }

    public string TextContent { get; set; } = string.Empty;
    public List<string> ListItems { get; } = new();
    public bool IsOrdered { get; set; }
    public StructuredTable? Table { get; set; }
    public List<ImageWithBbox> Images { get; } = new();

    /// <summary>
    /// Ordered list split olunca, bu parçanın orijinal listede kaçıncı item'dan başladığı.
    /// Örnek: liste 16 madde, ikinci parça 7. maddeden başlıyorsa StartIndex = 6 (0-bazlı).
    /// Renderer numbering = StartIndex + i + 1 → orijinal numarayla render edilir.
    /// Default 0 → "1."'den başlar (normal liste).
    /// </summary>
    public int StartIndex { get; set; }

    /// <summary>
    /// Mistral OCR markdown'ının bu block'a karşılık gelen TAM substring'i.
    /// Markdig'in node.Span ile alınır → re-render yok, orijinal yapı (nested list, bold,
    /// italic, link, inline code, custom format) %100 korunur.
    /// SplitOversizedBlockAsync ile bölünmüş sentetik piece'lerde null kalır → renderer fallback.
    /// </summary>
    public string? RawMarkdown { get; set; }

    /// <summary>
    /// Block'un sayfadaki TAHMİNİ dikey kapsamı (0.0 = sayfa tepesi, 1.0 = sayfa dibi).
    /// MarkdownBlockExtractor sayfa içi block'ların content-length oranıyla hesaplar
    /// (uniform spacing varsayımından daha doğru: uzun paragraflar büyük slot alır,
    /// kısa heading'ler küçük slot alır).
    ///
    /// ImageLinker görsel atamasında kullanır:
    ///   - image.NormY ∈ [BboxTopNormY, BboxBottomNormY] ise direkt eşleşir (içerme)
    ///   - değilse en yakın kenara mesafe = score
    ///   - bbox null ise eski uniform spacing fallback'i devreye girer
    ///
    /// Split'le üretilen sentetik piece'lerde null kalabilir (orijinal block parçalanmış).
    /// </summary>
    public double? BboxTopNormY { get; set; }
    public double? BboxBottomNormY { get; set; }
}
