
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using DocuChat.Application.Interfaces.Services;

namespace DocuChat.Infrastructure.Services.Ai;

public class EmbeddingService : IEmbeddingService
{
    private readonly HttpClient _http;
    private readonly string _model;
    private readonly ILogger<EmbeddingService> _logger;
    private readonly IMemoryCache _memCache;

    private static readonly MemoryCacheEntryOptions CacheOptions = new()
    {
        // Aynı sorgu tekrar gelirse Ollama'ya gitmeden çözülür.
        // Sliding: hareketli pencere — eski ama hâlâ sorulan girişler kalır.
        // Absolute: en geç 2 saatte yenilenir (model değişirse stale dönmesin).
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
            return hit;
        }

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

    private string BuildCacheKey(string text)
    {
        // SHA256: çakışmasız, model + metin → benzersiz anahtar.
        var bytes = Encoding.UTF8.GetBytes(text);
        var hash = SHA256.HashData(bytes);
        return $"emb:{_model}:{Convert.ToHexString(hash)}";
    }
}
