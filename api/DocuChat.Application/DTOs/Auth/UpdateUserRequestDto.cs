namespace DocuChat.Application.DTOs.Auth;

public record UpdateUserRequestDto(
    string FullName,
    string Email,
    string? Password);
