namespace DocuChat.Application.DTOs.Auth;

public class LoginRequestDto
{
    public string Email { get; set; }
    public string Password { get; set; }

    public LoginRequestDto(string Email, string Password)
    {
        this.Email = Email;
        this.Password = Password;
    }
}
