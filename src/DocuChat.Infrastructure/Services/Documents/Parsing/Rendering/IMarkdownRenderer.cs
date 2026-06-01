using DocuChat.Infrastructure.Services.Documents.Parsing.Models;

namespace DocuChat.Infrastructure.Services.Documents.Parsing.Rendering;

public interface IMarkdownRenderer
{
    string Render(SemanticBlock block);
    string Render(IEnumerable<SemanticBlock> blocks);
    string ToCleanText(SemanticBlock block);
}
