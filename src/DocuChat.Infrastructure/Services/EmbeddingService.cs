using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using DocuChat.Application.Abstractions;

namespace DocuChat.Infrastructure.Services;

public class EmbeddingService : IEmbeddingService
{
    private readonly HttpClient _http;
    private readonly string _model;

    public EmbeddingService(HttpClient http, IConfiguration cfg)
    {
        _http = http;
        _model = cfg["Embedding:Model"]!;

        if (_http.BaseAddress is null)
            _http.BaseAddress = new Uri(cfg["Embedding:BaseUrl"]!);
    }

    public async Task<float[]> GetEmbeddingAsync(string text, CancellationToken ct = default)
    {
        var payload = new { model = _model, prompt = text };

        var response = await _http.PostAsJsonAsync("/api/embeddings", payload, ct);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException(
                $"Embedding API hatası [{(int)response.StatusCode}] " +
                $"— Model: {_model}, BaseAddress: {_http.BaseAddress}, Body: {body}");
        }

        var json = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);

        return doc.RootElement
                  .GetProperty("embedding")
                  .EnumerateArray()
                  .Select(e => e.GetSingle())
                  .ToArray();
    }
}