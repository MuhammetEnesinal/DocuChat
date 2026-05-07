
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using DocuChat.Application.Interfaces.Services;

namespace DocuChat.Infrastructure.Services;

public class EmbeddingService : IEmbeddingService
{
    private readonly HttpClient _http;
    private readonly string _model;
    private readonly ILogger<EmbeddingService> _logger;

    public EmbeddingService(HttpClient http, IConfiguration cfg, ILogger<EmbeddingService> logger)
    {
        _http = http;
        _logger = logger;
        _model = cfg["Embedding:Model"]
            ?? throw new InvalidOperationException("Embedding:Model config eksik.");
    }

    public async Task<float[]> GetEmbeddingAsync(string text, CancellationToken ct = default)
    {
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

        _logger.LogDebug("[Embedding] Tamamlandı — {Dim} boyut", vector.Length);
        return vector;
    }
}