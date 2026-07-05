using Microsoft.ML.Tokenizers;
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

namespace DocuChat.Infrastructure.Services.Documents.Parsing.Chunking;

public sealed class TokenCounter : ITokenCounter
{
    private readonly Tokenizer _tokenizer = TiktokenTokenizer.CreateForModel("gpt-4");

    public int Count(string text) =>
        string.IsNullOrEmpty(text) ? 0 : _tokenizer.CountTokens(text);
}
