namespace DocuChat.Application.DTOs.Auth;

public record ResetPasswordRequest(string Email, string Token, string NewPassword);
