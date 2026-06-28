using Microsoft.AspNetCore.Identity;

namespace DocuChat.Infrastructure.Persistence.Identity;

// Identity standart IdentityRole — özel field yok (eski Description kullanılmadığı için kaldırıldı).
// İleride rol-bazlı metadata gerekirse buraya tekrar property eklenebilir.
public class AppRole : IdentityRole
{
}
