using DocuChat.Domain.Enums;

namespace DocuChat.Application.DTOs.Auth;

public class UpdateUserRequestDto
{
    public string FullName { get; set; }
    public string Email { get; set; }
    public string PersonnelCode { get; set; }

    // Güncellenecek rol: yalnız User veya Manager. Boşsa User varsayılır.
    public string Role { get; set; }

    // Kullanıcının departmanları (zorunlu, çoklu). Güncellemede mevcut atamalar bununla değiştirilir.
    public List<Guid> DepartmentIds { get; set; }

    public UpdateUserRequestDto(
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
