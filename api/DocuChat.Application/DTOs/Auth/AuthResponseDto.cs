using DocuChat.Application.DTOs.Departments;

namespace DocuChat.Application.DTOs.Auth;

public class AuthResponseDto
{
    public string Token { get; set; }
    public string UserId { get; set; }
    public string Email { get; set; }
    public string FullName { get; set; }
    public DateTime ExpiresAt { get; set; }
    public IEnumerable<string> Roles { get; set; }

    // Kullanıcının departmanları — frontend yönetici belge yükleme seçicisi ve profil için.
    public IEnumerable<DepartmentBriefDto> Departments { get; set; } = new List<DepartmentBriefDto>();

    public AuthResponseDto(
        string Token, string UserId, string Email, string FullName,
        DateTime ExpiresAt, IEnumerable<string> Roles)
    {
        this.Token = Token;
        this.UserId = UserId;
        this.Email = Email;
        this.FullName = FullName;
        this.ExpiresAt = ExpiresAt;
        this.Roles = Roles;
    }
}
