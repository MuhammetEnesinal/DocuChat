using DocuChat.Infrastructure.Services.Documents.Parsing.Models;

namespace DocuChat.Infrastructure.Services.Documents.Parsing.Ast;

public sealed class HeaderChainTracker
{
    private readonly List<(int Level, string Text)> _stack = new();

    public void Push(int level, string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        while (_stack.Count > 0 && _stack[^1].Level >= level)
            _stack.RemoveAt(_stack.Count - 1);

        _stack.Add((level, text.Trim()));
    }

    public HeaderChain Current => new(_stack.ToArray());

    public void Reset() => _stack.Clear();
}
