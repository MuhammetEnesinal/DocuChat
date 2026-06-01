namespace DocuChat.Application.ServiceContracts;

public record ParsedChunk(
    string Content,
    string? ImagePath = null,
    string? Header = null,
    string? CleanContent = null,
    int? PageNumber = null,
    string? StructuredTableJson = null,
    int? TokenCount = null,
    string? ContentHash = null);
