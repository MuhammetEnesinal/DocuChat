using Microsoft.ML.Tokenizers;
using DocuChat.Application.Interfaces.Services;

namespace DocuChat.Infrastructure.Services.Documents.Parsing.Chunking;

public sealed class TokenCounter : ITokenCounter
{
    private readonly Tokenizer _tokenizer = TiktokenTokenizer.CreateForModel("gpt-4");

    public int Count(string text) =>
        string.IsNullOrEmpty(text) ? 0 : _tokenizer.CountTokens(text);
}
