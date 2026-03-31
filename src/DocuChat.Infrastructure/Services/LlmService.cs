using System.Net.Http.Json;
using System.Text.Json;
using DocuChat.Application.Abstractions;
using Microsoft.Extensions.Configuration;

namespace DocuChat.Infrastructure.Services;

public class LlmService : ILlmService
{
    private readonly HttpClient _http;
    private readonly IConfiguration _cfg;

    public LlmService(HttpClient http, IConfiguration cfg)
    {
        _http = http;
        _cfg = cfg;
    }

    public async Task<string> AskAsync(
        string question, IEnumerable<string> contextChunks, CancellationToken ct = default)
    {
        var context = string.Join("\n\n---\n\n", contextChunks);

        var systemPrompt =
            "Sen bir doküman asistanısın. Sadece sana verilen bağlam bilgisini kullanarak " +
            "soruları yanıtla. Bağlamda cevap yoksa bunu belirt.";

        var userMessage = $"Bağlam:\n{context}\n\nSoru: {question}";

        return _cfg["Llm:Provider"] == "Anthropic"
            ? await CallAnthropicAsync(systemPrompt, userMessage, ct)
            : await CallOpenAiAsync(systemPrompt, userMessage, ct);
    }

    private async Task<string> CallAnthropicAsync(
        string system, string user, CancellationToken ct)
    {
        var payload = new
        {
            model = _cfg["Llm:Model"],
            max_tokens = int.Parse(_cfg["Llm:MaxTokens"]!),
            system,
            messages = new[] { new { role = "user", content = user } }
        };

        var response = await _http.PostAsJsonAsync(string.Empty, payload, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        return json.GetProperty("content")[0].GetProperty("text").GetString()!;
    }

    private async Task<string> CallOpenAiAsync(
        string system, string user, CancellationToken ct)
    {
        var payload = new
        {
            model = _cfg["Llm:Model"],
            messages = new[]
            {
                new { role = "system", content = system },
                new { role = "user",   content = user   }
            }
        };

        var response = await _http.PostAsJsonAsync(string.Empty, payload, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        return json
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString()!;
    }
}