namespace DocuChat.Infrastructure.Services.Documents.Parsing.Models;

public sealed record ImageWithBbox(
    string Path,
    int PageNumber,
    double NormY,
    double NormX,
    string Source,
    bool PlacedInMarkdown = false)
{
    public bool PlacedInMarkdown { get; set; } = PlacedInMarkdown;
}
