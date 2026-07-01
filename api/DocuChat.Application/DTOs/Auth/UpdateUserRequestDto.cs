namespace DocuChat.Application.DTOs.Auth;

public class UpdateUserRequestDto
{
    public string FullName { get; set; }
    public string Email { get; set; }
    public string? Password { get; set; }

    public UpdateUserRequestDto(string FullName, string Email, string? Password)
    {
        this.FullName = FullName;
        this.Email = Email;
        this.Password = Password;
    }
}
