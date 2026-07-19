using System.Text;
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

namespace DocuChat.Infrastructure.Services.Documents.Parsing.Chunking;

// Recursive separator hierarchy splitter — universal text/code yapı işaretleriyle çalışır.
// Sıfır dil-spesifik kural, sıfır kısaltma listesi.
// Sırayla daha küçük ayraçlara iner: paragraf → satır → kelime → karakter. Cümle ayracı yoktur;
// nokta gibi noktalama işaretleri dile göre değiştiği ve kod içinde yanlış bölme yarattığı için
// ayraç olarak kullanılmaz.
// Kelime ortası ancak son çare karakter bölmesinde bölünür; ondan önceki seviyeler kelimeyi korur.
internal static class RecursiveSplitter
{
    // Metin için MİNİMAL UNIVERSAL separators — sıfır punctuation listesi.
    // Türkçe/İngilizce/Çince/Arapça/hangi dil olursa olsun aynı çalışır.
    // \n\n: paragraf ayracı (markdown standardı)
    // \n  : satır ayracı (her metin)
    // " " : kelime ayracı (Latin alfabesi + Slavyanca + birçok dil)
    // ""  : son çare karakter (boşluksuz dev string için)
    public static readonly string[] TextSeparators =
    {
        "\n\n",   // paragraf
        "\n",     // satır
        " ",      // kelime (SON UNIVERSAL — kelime asla bölünmez)
        ""        // son çare: karakter
    };

    // Kod için minimum separator seti — punctuation içermez (kod içinde "obj.method()" yanlış bölünmesin).
    // \n\n çoğu fonksiyon/bloğu doğal olarak yakalar (boş satır = blok ayrımı).
    public static readonly string[] CodeSeparators =
    {
        "\n\n",   // boş satır (fonksiyon/blok ayrımı — Python, C#, JS, hepsinde universal)
        "\n",     // satır
        " ",      // token/kelime
        ""        // son çare
    };

    public static List<string> Split(
        string text,
        int maxTokens,
        ITokenCounter tokens,
        IReadOnlyList<string>? separators = null)
    {
        var seps = separators ?? TextSeparators;
        return SplitRecursive(text, maxTokens, seps, level: 0, tokens);
    }

    private static List<string> SplitRecursive(
        string text, int maxTokens, IReadOnlyList<string> separators, int level, ITokenCounter tokens)
    {
        if (string.IsNullOrEmpty(text)) return new();

        // Bütçeye sığıyorsa olduğu gibi dön
        if (tokens.Count(text) <= maxTokens) return new() { text };

        // Hiçbir separator işe yaramadıysa karakter split (son çare, pratikte gelmez)
        if (level >= separators.Count) return CharacterSplit(text, maxTokens);

        var sep = separators[level];
        if (string.IsNullOrEmpty(sep)) return CharacterSplit(text, maxTokens);

        var parts = text.Split(sep, StringSplitOptions.None);

        // Bu separator metinde yoksa → bir sonraki seviyeye geç
        if (parts.Length == 1) return SplitRecursive(text, maxTokens, separators, level + 1, tokens);

        // Separator'ı geri ekle (Split kaybetti) — son parça hariç
        for (var i = 0; i < parts.Length - 1; i++) parts[i] += sep;

        var result = new List<string>();
        var current = new StringBuilder();
        var currentTokens = 0;

        foreach (var part in parts)
        {
            if (string.IsNullOrEmpty(part)) continue;
            var partTokens = tokens.Count(part);

            // Tek parça bile bütçeyi aşıyor → flush current, daha derin separator ile recurse
            if (partTokens > maxTokens)
            {
                if (current.Length > 0)
                {
                    result.Add(current.ToString());
                    current.Clear();
                    currentTokens = 0;
                }
                var subParts = SplitRecursive(part, maxTokens, separators, level + 1, tokens);
                result.AddRange(subParts);
                continue;
            }

            // Eklemek bütçeyi aşıyor → flush, yeni başlat
            if (currentTokens + partTokens > maxTokens && current.Length > 0)
            {
                result.Add(current.ToString());
                current.Clear();
                currentTokens = 0;
            }

            current.Append(part);
            currentTokens += partTokens;
        }

        if (current.Length > 0) result.Add(current.ToString());
        return result;
    }

    // Boşluksuz dev string için son çare (asla buraya gelmemeli)
    private static List<string> CharacterSplit(string text, int maxTokens)
    {
        var charsPerChunk = Math.Max(1, (int)(maxTokens * 3.5));
        var windows = new List<string>();
        for (var i = 0; i < text.Length; i += charsPerChunk)
        {
            var take = Math.Min(charsPerChunk, text.Length - i);
            windows.Add(text.Substring(i, take));
        }
        return windows;
    }
}
