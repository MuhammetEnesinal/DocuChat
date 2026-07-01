namespace DocuChat.Infrastructure.Services.Documents.Parsing.Models;

public class ImageWithBbox
{
    public string Path { get; set; }
    public int PageNumber { get; set; }
    public double NormY { get; set; }
    public double NormX { get; set; }
    public string Source { get; set; }
    public bool PlacedInMarkdown { get; set; }

    public ImageWithBbox(
        string Path,
        int PageNumber,
        double NormY,
        double NormX,
        string Source,
        bool PlacedInMarkdown = false)
    {
        this.Path = Path;
        this.PageNumber = PageNumber;
        this.NormY = NormY;
        this.NormX = NormX;
        this.Source = Source;
        this.PlacedInMarkdown = PlacedInMarkdown;
    }
}
