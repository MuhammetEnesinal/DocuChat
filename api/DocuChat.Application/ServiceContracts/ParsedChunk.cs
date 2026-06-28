namespace DocuChat.Application.ServiceContracts;

public record ParsedChunk(
    string Content,
    string? ImagePath = null,
    string? Header = null,
    string? CleanContent = null,
    int? PageNumber = null);
