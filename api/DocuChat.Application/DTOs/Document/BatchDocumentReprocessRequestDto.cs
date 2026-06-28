namespace DocuChat.Application.DTOs.Document;

public record BatchDocumentReprocessRequestDto(IEnumerable<Guid> Ids);
