namespace DocuChat.Infrastructure.Services.Documents.Parsing.Models;

public sealed class SemanticBlock
{
    public SemanticBlock(int index, int pageNumber, BlockType type, HeaderChain headers)
    {
        Index = index;
        PageNumber = pageNumber;
        Type = type;
        Headers = headers;
    }

    public int Index { get; }
    public int PageNumber { get; }
    public BlockType Type { get; }
    public HeaderChain Headers { get; }

    public string TextContent { get; set; } = string.Empty;
    public List<string> ListItems { get; } = new();
    public bool IsOrdered { get; init; }
    public StructuredTable? Table { get; init; }
    public List<ImageWithBbox> Images { get; } = new();
}
