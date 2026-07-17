using Microsoft.AspNetCore.Identity;
using DocuChat.Domain.Entities.Departments;

namespace DocuChat.Infrastructure.Persistence.Identity;

public class AppUser : IdentityUser
{
    public string? FullName { get; set; }

    // Kullanıcının üye olduğu departmanlar (çoklu). Arama/erişim izolasyonu bu üyeliklere göre.
    public List<UserDepartment> UserDepartments { get; set; } = new();

    // Personel kodu — admin/Excel ile atanır; yeni kullanıcının ilk şifresi olarak kullanılır.
    // Unique index'lidir; null olabilir (PostgreSQL'de null değerler unique index'te çakışmaz).
    public string? PersonnelCode { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
