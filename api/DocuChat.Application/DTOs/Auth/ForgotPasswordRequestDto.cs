namespace DocuChat.Application.DTOs.Auth;

public class ForgotPasswordRequestDto
{
    public string Email { get; set; }

    public ForgotPasswordRequestDto(string Email)
    {
        this.Email = Email;
    }
}
