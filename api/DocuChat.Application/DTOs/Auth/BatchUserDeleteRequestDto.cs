namespace DocuChat.Application.DTOs.Auth;

public record BatchUserDeleteRequestDto(IEnumerable<string> Ids);
