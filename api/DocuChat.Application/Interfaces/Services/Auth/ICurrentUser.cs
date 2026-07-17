
namespace DocuChat.Application.Interfaces.Services.Auth;

public interface ICurrentUser
{
    string UserId { get; }
    string Email { get; }
    bool IsAuthenticated { get; }
    bool IsInRole(string role);

    // Kullanıcının üye olduğu departman ID'leri (JWT claim'lerinden). Arama/erişim izolasyonu
    // buna göre; admin için boş olabilir çünkü admin departman filtresini bypass eder.
    IReadOnlyList<Guid> DepartmentIds { get; }
}
