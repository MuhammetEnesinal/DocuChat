namespace DocuChat.Infrastructure.Services.Documents.Parsing.Models;

public class HeaderChain
{
    public IReadOnlyList<(int Level, string Text)> Items { get; set; }

    public HeaderChain(IReadOnlyList<(int Level, string Text)> Items)
    {
        this.Items = Items;
    }

    public static HeaderChain Empty { get; } = new(Array.Empty<(int, string)>());

    // Ardışık EŞDEĞER başlıkları tekile indirir. Belgelerde çok yaygın desen: belge adı (H1),
    // sayfa banner'ı (H1) ve bölüm başlığı (H2) aynı metnin varyantlarıdır
    // ("2.3 İŞTEN ÇIK BUTONU" / "2- 3 İŞTEN ÇIK BUTONU" / "2.3 - İŞTEN ÇIK BUTONU").
    // Bu şişik zincir embedding + LLM + [Bağlam] prompt'u + UI'ya dört yerden gürültü taşıyordu.
    // Eşdeğerlik deterministik (bulanık eşik yok): yalnız HARFLERİ tutup küçük harfe indirilmiş
    // hali birebir aynıysa aynı başlıktır → yalnız ARDIŞIK eşitler düşer, "Giriş" ile
    // "Giriş Testleri" gibi gerçekten farklı başlıklar asla birleşmez.
    public string ToPath()
    {
        if (Items.Count == 0) return string.Empty;

        var kept = new List<string>(Items.Count);
        var prevNorm = string.Empty;
        foreach (var (_, text) in Items)
        {
            var norm = NormalizeForCompare(text);
            if (kept.Count > 0 && norm.Length > 0 && norm == prevNorm) continue;
            kept.Add(text);
            prevNorm = norm;
        }
        return string.Join(" > ", kept);
    }

    private static string NormalizeForCompare(string s)
    {
        Span<char> buf = stackalloc char[s.Length];
        var n = 0;
        foreach (var ch in s)
            if (char.IsLetter(ch)) buf[n++] = char.ToLowerInvariant(ch);
        return new string(buf[..n]);
    }
}
