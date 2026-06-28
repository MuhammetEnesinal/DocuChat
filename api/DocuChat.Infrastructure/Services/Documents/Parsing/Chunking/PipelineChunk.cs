namespace DocuChat.Infrastructure.Services.Documents.Parsing.Chunking;

public sealed class PipelineChunk
{
    public string MarkdownContent { get; set; } = string.Empty;
    public string CleanContent { get; set; } = string.Empty;
    public string Header { get; set; } = string.Empty;
    public int PageNumber { get; set; }
    public List<string> ImagePaths { get; set; } = new();
}
