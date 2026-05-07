namespace DocuChat.Application.DTOs.Auth;

public record UpdateUserRequest(
    string FullName,
    string Email,
    string? Password);
