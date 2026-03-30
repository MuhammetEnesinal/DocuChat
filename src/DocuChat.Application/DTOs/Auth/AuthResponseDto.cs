namespace DocuChat.Application.DTOs.Auth;

public record AuthResponseDto(
    string Token,
    string UserId,
    string Email,
    string FullName,
    DateTime ExpiresAt,
    IEnumerable<string> Roles);