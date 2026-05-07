namespace DocuChat.Application.DTOs.Chat;

public record BatchDeleteRequest(IEnumerable<Guid> Ids);
