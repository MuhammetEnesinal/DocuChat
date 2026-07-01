using Microsoft.AspNetCore.Identity;

namespace DocuChat.Infrastructure.Persistence.Identity;

public class AppUser : IdentityUser
{
    public string? FullName { get; set; }

    // Personel kodu — admin/Excel ile atanır; yeni kullanıcının İLK ŞİFRESİ olarak kullanılır.
    // Benzersiz (unique index). Eski kayıtlarda null olabilir; PostgreSQL'de null'lar unique
    // index'te çakışmaz, bu yüzden mevcut kullanıcılar sorun çıkarmaz.
    public string? PersonnelCode { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
