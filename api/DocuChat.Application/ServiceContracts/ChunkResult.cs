namespace DocuChat.Application.ServiceContracts;

public record ChunkResult(
    string FileName,
    string Content,
    string? ImagePath = null,
    string? Header = null,
    int? PageNumber = null);
