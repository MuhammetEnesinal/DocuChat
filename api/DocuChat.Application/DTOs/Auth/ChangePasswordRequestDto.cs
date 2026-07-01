namespace DocuChat.Application.DTOs.Auth;

public class ChangePasswordRequestDto
{
    public string CurrentPassword { get; set; }
    public string NewPassword { get; set; }

    public ChangePasswordRequestDto(string CurrentPassword, string NewPassword)
    {
        this.CurrentPassword = CurrentPassword;
        this.NewPassword = NewPassword;
    }
}
