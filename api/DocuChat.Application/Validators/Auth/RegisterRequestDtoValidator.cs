using FluentValidation;
using DocuChat.Application.DTOs.Auth;

namespace DocuChat.Application.Validators.Auth;

public class RegisterRequestDtoValidator : AbstractValidator<RegisterRequestDto>
{
    public RegisterRequestDtoValidator()
    {
        RuleFor(x => x.FullName)
            .NotEmpty().WithMessage("Ad Soyad boş olamaz.")
            .MaximumLength(100).WithMessage("Ad Soyad en fazla 100 karakter olabilir.");

        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("E-posta boş olamaz.")
            .EmailAddress().WithMessage("Geçerli bir e-posta adresi giriniz.")
            .MaximumLength(256).WithMessage("E-posta en fazla 256 karakter olabilir.");

        // Personel kodu yeni kullanıcının ilk şifresi olur (örn EMP1001). Harf + rakam karışık,
        // boşluksuz. Büyük/küçük harf + sembol zorunlu değil. Bu kurallar hem tekil admin ekleme
        // hem Excel toplu import için geçerli (ProcessImportRow aynı validator'ı kullanır).
        RuleFor(x => x.PersonnelCode)
            .NotEmpty().WithMessage("Personel kodu boş olamaz.")
            .MinimumLength(6).WithMessage("Personel kodu en az 6 karakter olmalıdır.")
            .MaximumLength(50).WithMessage("Personel kodu en fazla 50 karakter olabilir.")
            .Matches(@"^\S+$").WithMessage("Personel kodu boşluk içeremez.")
            .Matches("[A-Za-zÇĞİÖŞÜçğıöşü]").WithMessage("Personel kodu en az bir harf içermelidir.")
            .Matches("[0-9]").WithMessage("Personel kodu en az bir rakam içermelidir.");
    }
}
