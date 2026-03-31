namespace DocuChat.Application.Abstractions;

public interface ILlmService
{
    Task<string> AskAsync(string question, IEnumerable<string> contextChunks, CancellationToken ct = default);
}