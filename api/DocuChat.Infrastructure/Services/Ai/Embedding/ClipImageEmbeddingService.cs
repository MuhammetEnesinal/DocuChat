using System.Net.Http.Json;
using System.Text.Json.Serialization;
using DocuChat.Application.Interfaces.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace DocuChat.Infrastructure.Services.Ai.Embedding;

/// <summary>
/// CLIP görsel embedding — yerel sidecar'a (rerank-service, port 8085) HTTP ile bağlanır.
/// /embed-image ve /embed-text endpoint'leri resim ve metni aynı 512-dim uzaya koyar.
/// Sidecar erişilemezse fail-open: null döner, çağıran taraf CLIP'siz davranışa düşer.
/// </summary>
public sealed class ClipImageEmbeddingService : IImageEmbeddingService
{
    private readonly HttpClient _http;
    private readonly ILogger<ClipImageEmbeddingService> _logger;

    public bool Enabled { get; }

    private const int ExpectedDim = 512;

    public ClipImageEmbeddingService(
        HttpClient http, IConfiguration cfg, ILogger<ClipImageEmbeddingService> logger)
    {
        _http = http;
        _logger = logger;
        Enabled = cfg.GetValue("ImageEmbedding:Enabled", true);
    }

    public async Task<IReadOnlyList<float[]?>> EmbedImagesAsync(
        IReadOnlyList<byte[]> images, CancellationToken ct = default)
    {
        if (!Enabled || images.Count == 0) return Array.Empty<float[]?>();

        var payload = new { images_base64 = images.Select(Convert.ToBase64String).ToList() };
        try
        {
            using var resp = await _http.PostAsJsonAsync("/embed-image", payload, ct);
            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogWarning("[CLIP] /embed-image HTTP {S} — {N} resim embed edilemedi",
                    (int)resp.StatusCode, images.Count);
                return AllNull(images.Count);
            }

            var body = await resp.Content.ReadFromJsonAsync<EmbedResponse>(cancellationToken: ct);
            return ParseVectors(body, images.Count);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[CLIP] /embed-image hatası — sidecar erişilemez olabilir");
            return AllNull(images.Count);
        }
    }

    public async Task<float[]?> EmbedTextAsync(string text, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var vecs = await EmbedTextsAsync(new[] { text }, ct);
        return vecs.Count > 0 ? vecs[0] : null;
    }

    public async Task<IReadOnlyList<float[]?>> EmbedTextsAsync(
        IReadOnlyList<string> texts, CancellationToken ct = default)
    {
        if (!Enabled || texts.Count == 0) return Array.Empty<float[]?>();

        try
        {
            using var resp = await _http.PostAsJsonAsync("/embed-text", new { texts }, ct);
            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogWarning("[CLIP] /embed-text HTTP {S}", (int)resp.StatusCode);
                return AllNull(texts.Count);
            }

            var body = await resp.Content.ReadFromJsonAsync<EmbedResponse>(cancellationToken: ct);
            return ParseVectors(body, texts.Count);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[CLIP] /embed-text hatası");
            return AllNull(texts.Count);
        }
    }

    private static IReadOnlyList<float[]?> ParseVectors(EmbedResponse? body, int count)
    {
        var result = new float[]?[count];
        if (body?.Vectors is null) return result;
        for (var i = 0; i < count && i < body.Vectors.Count; i++)
        {
            var v = body.Vectors[i];
            result[i] = v is { Count: ExpectedDim } ? v.ToArray() : null;  // boş/eksik = null
        }
        return result;
    }

    private static float[]?[] AllNull(int count) => new float[]?[count];

    private sealed record EmbedResponse(
        [property: JsonPropertyName("vectors")] List<List<float>> Vectors);
}
