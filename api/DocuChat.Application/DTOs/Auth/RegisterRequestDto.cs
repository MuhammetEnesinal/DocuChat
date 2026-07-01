namespace DocuChat.Application.DTOs.Auth;

// Admin kullanıcı oluşturma / Excel import DTO'su (self-register KULLANMAZ).
public class RegisterRequestDto
{
    public string FullName { get; set; }
    public string Email { get; set; }
    // Personel kodu — yeni kullanıcının İLK ŞİFRESİ olarak kullanılır (admin ayrı şifre girmez).
    public string PersonnelCode { get; set; }

    public RegisterRequestDto(string FullName, string Email, string PersonnelCode)
    {
        this.FullName = FullName;
        this.Email = Email;
        this.PersonnelCode = PersonnelCode;
    }
}
