namespace DocuChat.Application.DTOs.Document;

public record BatchDocumentDeleteRequestDto(IEnumerable<Guid> Ids);
