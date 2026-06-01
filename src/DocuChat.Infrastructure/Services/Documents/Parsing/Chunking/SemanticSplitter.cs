using DocuChat.Application.Interfaces.Services;

namespace DocuChat.Infrastructure.Services.Documents.Parsing.Chunking;

public sealed class SemanticSplitter
{
    private readonly IEmbeddingService _embedder;
    private readonly ITokenCounter _tokens;
    private readonly int _windowTokens;
    private readonly double _breakpointPercentile;

    public SemanticSplitter(
        IEmbeddingService embedder,
        ITokenCounter tokens,
        int windowTokens = 80,
        double breakpointPercentile = 0.85)
    {
        _embedder = embedder;
        _tokens = tokens;
        _windowTokens = windowTokens;
        _breakpointPercentile = breakpointPercentile;
    }

    public async Task<List<string>> SplitAsync(
        string text, int maxTokens, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text)) return new();

        var totalTokens = _tokens.Count(text);
        if (totalTokens <= maxTokens) return new() { text };

        // 1) Token-bütçeli pencerelere ayır (yalnız sayım, dil yok)
        var windows = SplitIntoWindows(text, _windowTokens);
        if (windows.Count == 1) return windows;

        // 2) Tüm pencereleri batch olarak embed et (paralel)
        var embedTasks = windows.Select(w => _embedder.GetEmbeddingAsync(w, ct)).ToArray();
        var embeddings = await Task.WhenAll(embedTasks);

        // 3) Ardışık pencereler arası kosinüs uzaklığı hesapla
        var distances = new double[windows.Count - 1];
        for (var i = 0; i < distances.Length; i++)
            distances[i] = 1.0 - CosineSimilarity(embeddings[i], embeddings[i + 1]);

        // 4) Breakpoint eşiği — uzaklık dağılımının üst yüzdeliği (percentile)
        var threshold = Percentile(distances, _breakpointPercentile);

        // 5) Pencereleri yürü: chunk biriktir; uzaklık eşiği aşarsa VEYA token bütçesi dolarsa yeni chunk
        var chunks = new List<string>();
        var current = new System.Text.StringBuilder();
        var currentTokens = 0;
        var currentStart = 0;

        for (var i = 0; i < windows.Count; i++)
        {
            var winTokens = _tokens.Count(windows[i]);

            var shouldBreak =
                currentTokens > 0 && (
                    currentTokens + winTokens > maxTokens
                    || (i > 0 && distances[i - 1] > threshold)
                );

            if (shouldBreak)
            {
                chunks.Add(current.ToString().Trim());
                current.Clear();
                currentTokens = 0;
                currentStart = i;
            }

            if (current.Length > 0) current.Append(' ');
            current.Append(windows[i]);
            currentTokens += winTokens;
        }

        if (current.Length > 0)
            chunks.Add(current.ToString().Trim());

        return chunks.Where(c => c.Length > 0).ToList();
    }


    private static List<string> SplitIntoWindows(string text, int windowTokens)
    {
        // Token sayımı için pencere büyüklüğünü karakter cinsine yaklaştır
        var charsPerWindow = Math.Max(1, (int)(windowTokens * 3.5));
        var windows = new List<string>();
        for (var i = 0; i < text.Length; i += charsPerWindow)
        {
            var take = Math.Min(charsPerWindow, text.Length - i);
            windows.Add(text.Substring(i, take));
        }
        return windows;
    }

    private static double CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length || a.Length == 0) return 0.0;
        double dot = 0, na = 0, nb = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            na += a[i] * a[i];
            nb += b[i] * b[i];
        }
        var denom = Math.Sqrt(na) * Math.Sqrt(nb);
        return denom == 0 ? 0.0 : dot / denom;
    }

    private static double Percentile(double[] values, double p)
    {
        if (values.Length == 0) return 0.0;
        var sorted = values.OrderBy(v => v).ToArray();
        var idx = (int)Math.Ceiling(p * sorted.Length) - 1;
        return sorted[Math.Clamp(idx, 0, sorted.Length - 1)];
    }
}
