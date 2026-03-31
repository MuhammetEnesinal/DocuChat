using System.Net.Http.Json;
using System.Text.Json;
using DocuChat.Application.Abstractions;
using Microsoft.Extensions.Configuration;

namespace DocuChat.Infrastructure.Services;

public class EmbeddingService : IEmbeddingService
{
    private readonly HttpClient _http;
    private readonly IConfiguration _cfg;

    public EmbeddingService(HttpClient http, IConfiguration cfg)
    {
        _http = http;
        _cfg = cfg;
    }

    public async Task<float[]> GetEmbeddingAsync(string text, CancellationToken ct = default)
    {
        var payload = new
        {
            model = _cfg["Embedding:Model"],
            input = text,
        };

        var response = await _http.PostAsJsonAsync(string.Empty, payload, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);

        return json
            .GetProperty("data")[0]
            .GetProperty("embedding")
            .EnumerateArray()
            .Select(e => e.GetSingle())
            .ToArray();
    }
}