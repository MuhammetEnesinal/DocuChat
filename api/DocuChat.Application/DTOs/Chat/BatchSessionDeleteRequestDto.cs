namespace DocuChat.Application.DTOs.Chat;

public record BatchSessionDeleteRequestDto(IEnumerable<Guid> Ids);
