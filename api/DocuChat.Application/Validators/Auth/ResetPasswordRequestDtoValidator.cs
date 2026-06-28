using FluentValidation;
using DocuChat.Application.DTOs.Auth;

namespace DocuChat.Application.Validators.Auth;

public class ResetPasswordRequestDtoValidator : AbstractValidator<ResetPasswordRequestDto>
{
    public ResetPasswordRequestDtoValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("E-posta boş olamaz.")
            .EmailAddress().WithMessage("Geçerli bir e-posta adresi giriniz.");

        RuleFor(x => x.Token)
            .NotEmpty().WithMessage("Sıfırlama kodu boş olamaz.");

        RuleFor(x => x.NewPassword)
            .NotEmpty().WithMessage("Şifre boş olamaz.")
            .MinimumLength(8).WithMessage("Şifre en az 8 karakter olmalıdır.")
            .Matches("[A-ZÇĞİÖŞÜ]").WithMessage("Şifre en az bir büyük harf içermelidir.")
            .Matches("[a-zçğıöşü]").WithMessage("Şifre en az bir küçük harf içermelidir.")
            .Matches("[0-9]").WithMessage("Şifre en az bir rakam içermelidir.")
            .Matches("[^a-zA-ZÇĞİÖŞÜçğıöşü0-9]").WithMessage("Şifre en az bir özel karakter içermelidir.");
    }
}
