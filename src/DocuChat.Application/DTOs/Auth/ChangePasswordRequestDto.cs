namespace DocuChat.Application.DTOs.Auth;

public record ChangePasswordRequestDto(string CurrentPassword, string NewPassword);
