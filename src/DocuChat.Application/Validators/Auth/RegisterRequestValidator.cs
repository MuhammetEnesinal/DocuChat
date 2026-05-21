using FluentValidation;
using DocuChat.Application.DTOs.Auth;

namespace DocuChat.Application.Validators.Auth;

public class RegisterRequestValidator : AbstractValidator<RegisterRequest>
{
    public RegisterRequestValidator()
    {
        RuleFor(x => x.FullName)
            .NotEmpty().WithMessage("Ad Soyad boş olamaz.")
            .MaximumLength(100).WithMessage("Ad Soyad en fazla 100 karakter olabilir.");

        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("E-posta boş olamaz.")
            .EmailAddress().WithMessage("Geçerli bir e-posta adresi giriniz.");

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Şifre boş olamaz.")
            .MinimumLength(8).WithMessage("Şifre en az 8 karakter olmalıdır.")
            .Matches("[A-ZÇĞİÖŞÜ]").WithMessage("En az bir büyük harf içermelidir.")
            .Matches("[a-zçğıöşü]").WithMessage("En az bir küçük harf içermelidir.")
            .Matches("[0-9]").WithMessage("En az bir rakam içermelidir.")
            .Matches("[^a-zA-ZÇĞİÖŞÜçğıöşü0-9]").WithMessage("En az bir özel karakter içermelidir.");
    }
}