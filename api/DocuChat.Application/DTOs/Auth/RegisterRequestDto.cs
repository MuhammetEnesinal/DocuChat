using DocuChat.Domain.Enums;

namespace DocuChat.Application.DTOs.Auth;

// Admin kullanıcı oluşturma / Excel import DTO'su (self-register KULLANMAZ).
public class RegisterRequestDto
{
    public string FullName { get; set; }
    public string Email { get; set; }
    // Personel kodu — yeni kullanıcının İLK ŞİFRESİ olarak kullanılır (admin ayrı şifre girmez).
    public string PersonnelCode { get; set; }

    // Atanacak rol: yalnız User veya Manager (Admin bu yoldan atanamaz). Boşsa User varsayılır.
    public string Role { get; set; }

    // Kullanıcının atanacağı departmanlar (zorunlu — departmansız kullanıcı yok, çoklu olabilir).
    public List<Guid> DepartmentIds { get; set; }

    public RegisterRequestDto(
        string FullName, string Email, string PersonnelCode,
        string? Role = null, List<Guid>? DepartmentIds = null)
    {
        this.FullName = FullName;
        this.Email = Email;
        this.PersonnelCode = PersonnelCode;
        this.Role = string.IsNullOrWhiteSpace(Role) ? Roles.User : Role;
        this.DepartmentIds = DepartmentIds ?? new List<Guid>();
    }
}
