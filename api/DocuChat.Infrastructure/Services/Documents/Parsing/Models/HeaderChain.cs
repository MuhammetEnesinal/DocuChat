namespace DocuChat.Infrastructure.Services.Documents.Parsing.Models;

public class HeaderChain
{
    public IReadOnlyList<(int Level, string Text)> Items { get; set; }

    public HeaderChain(IReadOnlyList<(int Level, string Text)> Items)
    {
        this.Items = Items;
    }

    public static HeaderChain Empty { get; } = new(Array.Empty<(int, string)>());

    public string ToPath() => Items.Count == 0
        ? string.Empty
        : string.Join(" > ", Items.Select(i => i.Text));
}
