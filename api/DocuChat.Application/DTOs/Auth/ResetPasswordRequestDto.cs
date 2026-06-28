namespace DocuChat.Application.DTOs.Auth;

public record ResetPasswordRequestDto(string Email, string Token, string NewPassword);
