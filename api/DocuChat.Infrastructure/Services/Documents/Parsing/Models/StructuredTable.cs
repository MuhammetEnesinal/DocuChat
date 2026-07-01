namespace DocuChat.Infrastructure.Services.Documents.Parsing.Models;

public class StructuredTable
{
    public IReadOnlyList<string> Headers { get; set; }
    public IReadOnlyList<IReadOnlyDictionary<string, string>> Rows { get; set; }

    public StructuredTable(
        IReadOnlyList<string> Headers,
        IReadOnlyList<IReadOnlyDictionary<string, string>> Rows)
    {
        this.Headers = Headers;
        this.Rows = Rows;
    }
}
