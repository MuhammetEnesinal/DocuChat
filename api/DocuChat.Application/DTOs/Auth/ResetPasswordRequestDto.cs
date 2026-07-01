namespace DocuChat.Application.DTOs.Auth;

public class ResetPasswordRequestDto
{
    public string Email { get; set; }
    public string Token { get; set; }
    public string NewPassword { get; set; }

    public ResetPasswordRequestDto(string Email, string Token, string NewPassword)
    {
        this.Email = Email;
        this.Token = Token;
        this.NewPassword = NewPassword;
    }
}
