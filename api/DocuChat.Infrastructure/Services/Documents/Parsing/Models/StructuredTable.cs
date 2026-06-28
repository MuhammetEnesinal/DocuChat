namespace DocuChat.Infrastructure.Services.Documents.Parsing.Models;

public sealed record StructuredTable(
    IReadOnlyList<string> Headers,
    IReadOnlyList<IReadOnlyDictionary<string, string>> Rows);
