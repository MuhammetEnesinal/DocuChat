namespace DocuChat.Application.ServiceContracts;

public class ParsedChunk
{
    public string Content { get; set; }
    public string? ImagePath { get; set; }
    public string? Header { get; set; }
    public string? CleanContent { get; set; }
    public int? PageNumber { get; set; }

    public ParsedChunk(
        string Content,
        string? ImagePath = null,
        string? Header = null,
        string? CleanContent = null,
        int? PageNumber = null)
    {
        this.Content = Content;
        this.ImagePath = ImagePath;
        this.Header = Header;
        this.CleanContent = CleanContent;
        this.PageNumber = PageNumber;
    }
}
