
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
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

namespace DocuChat.Infrastructure.Services.Ai.Embedding;

public class EmbeddingService : IEmbeddingService
{
    private readonly HttpClient _http;
    private readonly string _model;
    private readonly ILogger<EmbeddingService> _logger;
    private readonly IMemoryCache _memCache;

    // Cache hit/miss istatistikleri — production'da LogDebug görünmediği için periyodik
    // LogInformation ile hit rate'i izleyebiliriz. Thread-safe Interlocked.
    private long _hits;
    private long _misses;
    private const int StatsLogEvery = 100;

    private static readonly MemoryCacheEntryOptions CacheOptions = new()
    {
        // Aynı sorgu tekrar gelirse Ollama'ya gitmeden çözülür.
        SlidingExpiration = TimeSpan.FromMinutes(30),
        AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(2),
        Size = 1,
    };

    public EmbeddingService(
        HttpClient http,
        IConfiguration cfg,
        IMemoryCache memCache,
        ILogger<EmbeddingService> logger)
    {
        _http = http;
        _memCache = memCache;
        _logger = logger;
        _model = cfg["Embedding:Model"]
            ?? throw new InvalidOperationException("Embedding:Model config eksik.");
    }

    public async Task<float[]> GetEmbeddingAsync(string text, CancellationToken ct = default)
    {
        var key = BuildCacheKey(text);
        if (_memCache.TryGetValue(key, out float[]? hit) && hit is not null)
        {
            _logger.LogDebug("[Embedding] Cache HIT — TextLen: {Len}", text.Length);
            RecordCacheHitStats(hit: true);
            return hit;
        }

        RecordCacheHitStats(hit: false);
        _logger.LogDebug("[Embedding] İstek gönderiliyor — Model: {Model}, TextLen: {Len}", _model, text.Length);

        var payload = new { model = _model, prompt = text };
        var response = await _http.PostAsJsonAsync("/api/embeddings", payload, ct);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError("[Embedding] API hatası [{Status}] — Model: {Model}, Body: {Body}",
                (int)response.StatusCode, _model, body);
            throw new HttpRequestException(
                $"Embedding API hatası [{(int)response.StatusCode}] " +
                $"— Model: {_model}, BaseAddress: {_http.BaseAddress}, Body: {body}");
        }

        var json = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);

        var vector = doc.RootElement
                        .GetProperty("embedding")
                        .EnumerateArray()
                        .Select(e => e.GetSingle())
                        .ToArray();

        _memCache.Set(key, vector, CacheOptions);
        _logger.LogDebug("[Embedding] Tamamlandı — {Dim} boyut (cache yazıldı)", vector.Length);
        return vector;
    }

    // Ollama batch endpoint /api/embed başına gönderilecek max metin sayısı. Çok büyük tek
    // istek (örn 600 metin) timeout/bellek riski → güvenli dilimlere böl.
    private const int MaxBatchSize = 64;

    public async Task<IReadOnlyList<float[]?>> GetEmbeddingsAsync(
        IReadOnlyList<string> texts, CancellationToken ct = default)
    {
        if (texts.Count == 0) return Array.Empty<float[]?>();

        var results = new float[texts.Count][];
        var missIndices = new List<int>();
        var missTexts = new List<string>();

        // [1] Cache kontrolü — hit olanları doldur, miss olanları topla
        for (var i = 0; i < texts.Count; i++)
        {
            var key = BuildCacheKey(texts[i]);
            if (_memCache.TryGetValue(key, out float[]? hit) && hit is not null)
            {
                results[i] = hit;
                RecordCacheHitStats(hit: true);
            }
            else
            {
                RecordCacheHitStats(hit: false);
                missIndices.Add(i);
                missTexts.Add(texts[i]);
            }
        }

        if (missTexts.Count == 0) return results;  // hepsi cache'te — ağ çağrısı yok

        // [2] Miss'leri MaxBatchSize'lık dilimlerde /api/embed ile toplu çöz
        for (var start = 0; start < missTexts.Count; start += MaxBatchSize)
        {
            ct.ThrowIfCancellationRequested();
            var sliceTexts = missTexts.GetRange(start, Math.Min(MaxBatchSize, missTexts.Count - start));
            var sliceIndices = missIndices.GetRange(start, sliceTexts.Count);

            float[][]? vectors = null;
            try { vectors = await CallBatchEmbedAsync(sliceTexts, ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "[Embedding] Batch endpoint hatası — tekil yola düşülüyor ({N} metin)", sliceTexts.Count);
            }

            if (vectors is not null && vectors.Length == sliceTexts.Count)
            {
                // Batch başarılı → cache + sonuç (input sırası korunur)
                for (var j = 0; j < sliceTexts.Count; j++)
                {
                    results[sliceIndices[j]] = vectors[j];
                    _memCache.Set(BuildCacheKey(sliceTexts[j]), vectors[j], CacheOptions);
                }
            }
            else
            {
                // Fallback: batch desteklenmiyorsa tekil endpoint ile devam edilir.
                _logger.LogInformation(
                    "[Embedding] Batch sonuç alınamadı — {N} metin tekil işlenecek", sliceTexts.Count);
                for (var j = 0; j < sliceTexts.Count; j++)
                {
                    try { results[sliceIndices[j]] = await GetEmbeddingAsync(sliceTexts[j], ct); }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                    catch (Exception ex)
                    {
                        // Tekil çağrı da başarısız olursa ilgili eleman null kalır; çağıran taraf
                        // o chunk'ı atlar ve belge işlenmeye devam eder.
                        _logger.LogWarning(ex, "[Embedding] Tekil fallback başarısız — metin atlanacak");
                    }
                }
            }
        }

        _logger.LogDebug("[Embedding] Batch tamamlandı — {Total} metin ({Miss} ağ, {Hit} cache)",
            texts.Count, missTexts.Count, texts.Count - missTexts.Count);
        return results;
    }

    // Ollama /api/embed — input: [...] çoğul, embeddings: [[...],[...]] döner.
    // Başarısız/format dışı yanıtta null → caller tekil yola düşer.
    private async Task<float[][]?> CallBatchEmbedAsync(IReadOnlyList<string> texts, CancellationToken ct)
    {
        var payload = new { model = _model, input = texts };
        var response = await _http.PostAsJsonAsync("/api/embed", payload, ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            _logger.LogWarning("[Embedding] Batch API [{Status}]: {Body}",
                (int)response.StatusCode, body.Length > 200 ? body[..200] : body);
            return null;
        }

        var json = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("embeddings", out var embsEl)
            || embsEl.ValueKind != JsonValueKind.Array)
            return null;

        var vectors = new List<float[]>(texts.Count);
        foreach (var embEl in embsEl.EnumerateArray())
            vectors.Add(embEl.EnumerateArray().Select(e => e.GetSingle()).ToArray());
        return vectors.ToArray();
    }

    private string BuildCacheKey(string text)
    {
        // SHA256: çakışmasız, model + metin → benzersiz anahtar.
        var bytes = Encoding.UTF8.GetBytes(text);
        var hash = SHA256.HashData(bytes);
        return $"emb:{_model}:{Convert.ToHexString(hash)}";
    }

    private void RecordCacheHitStats(bool hit)
    {
        if (hit) System.Threading.Interlocked.Increment(ref _hits);
        else System.Threading.Interlocked.Increment(ref _misses);
        var grandTotal = System.Threading.Interlocked.Read(ref _hits) + System.Threading.Interlocked.Read(ref _misses);
        if (grandTotal % StatsLogEvery == 0)
        {
            var h = System.Threading.Interlocked.Read(ref _hits);
            var m = System.Threading.Interlocked.Read(ref _misses);
            var rate = (h + m) == 0 ? 0 : (double)h / (h + m);
            _logger.LogInformation(
                "[Embedding][CacheStats] {Total} call → {Hits} hit / {Misses} miss (hit rate: {Rate:P1})",
                h + m, h, m, rate);
        }
    }
}
