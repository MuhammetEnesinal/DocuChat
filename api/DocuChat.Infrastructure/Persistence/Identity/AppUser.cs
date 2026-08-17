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

    // "Yumuşak" claims damgası — departman/rol değişince döndürülür. JWT'de cstamp claim'i olarak
    // taşınır ve her istekte doğrulanır (eski token 401 olur), ANCAK /refresh bu damgayı ATLAYIP
    // yeni claim'lerle token basar → kesintisiz yetki güncellemesi. Buna karşılık şifre/e-posta
    // değişimi Identity'nin SecurityStamp'ini (SERT damga) döndürür → /refresh de reddedilir →
    // tüm cihazlardan çıkış. İki damganın ayrımı bu iki davranışı mümkün kılar.
    public string ClaimsStamp { get; set; } = Guid.NewGuid().ToString("N");

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
