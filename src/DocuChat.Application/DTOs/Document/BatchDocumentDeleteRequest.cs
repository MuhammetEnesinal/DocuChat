namespace DocuChat.Application.DTOs.Document;

public record BatchDocumentDeleteRequest(IEnumerable<Guid> Ids);
