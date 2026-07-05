namespace DocuChat.Infrastructure.Services.Documents.Parsing.Models;

public class ImageWithBbox
{
    public string Path { get; set; }
    public int PageNumber { get; set; }
    public double NormY { get; set; }
    public string Source { get; set; }

    public ImageWithBbox(
        string Path,
        int PageNumber,
        double NormY,
        string Source)
    {
        this.Path = Path;
        this.PageNumber = PageNumber;
        this.NormY = NormY;
        this.Source = Source;
    }
}
