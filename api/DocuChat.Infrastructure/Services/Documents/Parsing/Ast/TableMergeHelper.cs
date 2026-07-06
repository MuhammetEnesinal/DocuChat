using DocuChat.Infrastructure.Services.Documents.Parsing.Models;

namespace DocuChat.Infrastructure.Services.Documents.Parsing.Ast;

// Çok-sayfalı / çok-parçalı tabloların birleştirilmesindeki ORTAK mantık.
// Hem sayfa-içi (TableCoalescer) hem sayfa-sınırı (BlockMerger) birleştirme buradan beslenir;
// böylece tablonun kaç parçaya / kaç sayfaya bölündüğü fark etmeksizin aynı sağlam kurallar uygulanır.
// Mistral OCR bir tabloyu parçalara böldüğünde devam parçası üç kılıkta gelebilir:
// • HEADER TEKRARI    : devam parçası aynı başlık satırını yeniden basar → tekrar başlık atılır.
// • TAM AUTO-HEADER   : Markdig başlık bulamayıp "col1,col2,col3" üretmiş; başlık YOK, direkt veri
// → bu sahte başlık atılır, satırlar veri olarak alınır.
// • KISMİ AUTO-HEADER : devam parçasının İLK VERİ SATIRI başlık sanılmış; yalnız boş hücreler "colN",
// diğerleri gerçek veri (örn. [7, col2, Tornavida, Vida]) → bu satır KAYIP
// EDİLMEZ, veri satırına çevrilir.
// Satırlar, parçanın kendi kolon adlarından ana tablonun kolonlarına POZİSYONEL eşlenir
// (Rows header-adıyla anahtarlı olduğundan zorunlu). Bilgi kaybı olmaz.
internal static class TableMergeHelper
{
    // b, a'nın devam parçası mı? (aynı kolon sayısı + [herhangi colN] ya da [aynı başlık])
    public static bool IsContinuation(StructuredTable a, StructuredTable b)
    {
        if (a.Headers.Count == 0 || a.Headers.Count != b.Headers.Count) return false;
        if (HasAnyAutoHeader(b.Headers)) return true;
        return HeadersEqual(a.Headers, b.Headers);
    }

    // a + b → tek tablo. a'nın kolon adları korunur; b'nin başlığı (sahte ise) veri satırına çevrilir;
    // b'nin tüm satırları a'nın kolonlarına pozisyonel eşlenir. Görsel birleştirme ÇAĞIRANIN işidir.
    public static StructuredTable Merge(StructuredTable a, StructuredTable b)
    {
        var aHeaders = a.Headers;
        var bHeaders = b.Headers;

        var rows = new List<IReadOnlyDictionary<string, string>>(a.Rows);

        // b'nin "başlığı" gerçekten başlık mı, yoksa veri satırı mı?
        //   - Tam auto-header (col1..colN) → Markdig uydurması, veri değil → at.
        //   - a ile birebir aynı → başlık tekrarı → at.
        //   - aksi (kısmi colN veya farklı gerçek değer) → veri satırıdır → ekle (kayıp olmasın).
        var headerIsRepeat = !HasAnyAutoHeader(bHeaders) && HeadersEqual(aHeaders, bHeaders);
        var headerIsDiscardable = IsAllAutoHeader(bHeaders) || headerIsRepeat;
        if (!headerIsDiscardable)
            rows.Add(HeaderValuesToRow(aHeaders, bHeaders));

        foreach (var row in b.Rows)
            rows.Add(RemapRow(aHeaders, bHeaders, row));

        return new StructuredTable(aHeaders, rows);
    }

    // En az bir kolon "colN" placeholder mı? (Markdig boş/eksik başlık hücresine colN verir.)
    private static bool HasAnyAutoHeader(IReadOnlyList<string> headers)
    {
        for (var idx = 0; idx < headers.Count; idx++)
            if (string.Equals(headers[idx], $"col{idx + 1}", StringComparison.Ordinal)) return true;
        return false;
    }

    // Tüm kolonlar "colN" mı? (Devam parçasında başlık hiç yok → satır gerçek veri değil, atılır.)
    private static bool IsAllAutoHeader(IReadOnlyList<string> headers)
    {
        if (headers.Count == 0) return false;
        for (var idx = 0; idx < headers.Count; idx++)
            if (!string.Equals(headers[idx], $"col{idx + 1}", StringComparison.Ordinal)) return false;
        return true;
    }

    private static bool HeadersEqual(IReadOnlyList<string> a, IReadOnlyList<string> b)
    {
        if (a.Count != b.Count) return false;
        for (var idx = 0; idx < a.Count; idx++)
            if (!string.Equals(a[idx], b[idx], StringComparison.OrdinalIgnoreCase)) return false;
        return true;
    }

    // b'nin başlık DEĞERLERİNİ ana kolonlara pozisyonel yerleştirip veri satırı üretir.
    // "colN" placeholder hücreler boş kabul edilir.
    private static Dictionary<string, string> HeaderValuesToRow(
        IReadOnlyList<string> aHeaders, IReadOnlyList<string> bHeaderValues)
    {
        var row = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var idx = 0; idx < aHeaders.Count; idx++)
        {
            var v = idx < bHeaderValues.Count ? bHeaderValues[idx] : string.Empty;
            if (string.Equals(v, $"col{idx + 1}", StringComparison.Ordinal)) v = string.Empty;
            row[aHeaders[idx]] = v;
        }
        return row;
    }

    // b'nin bir satırını, kendi kolon adlarından (bHeaders) ana kolonlara (aHeaders) pozisyonel eşler.
    private static Dictionary<string, string> RemapRow(
        IReadOnlyList<string> aHeaders, IReadOnlyList<string> bHeaders,
        IReadOnlyDictionary<string, string> row)
    {
        var remapped = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var idx = 0; idx < aHeaders.Count; idx++)
        {
            var bKey = idx < bHeaders.Count ? bHeaders[idx] : null;
            var v = bKey != null && row.TryGetValue(bKey, out var val) ? val : string.Empty;
            remapped[aHeaders[idx]] = v;
        }
        return remapped;
    }
}
