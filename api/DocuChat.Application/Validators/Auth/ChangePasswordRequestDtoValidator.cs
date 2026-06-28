using FluentValidation;
using DocuChat.Application.DTOs.Auth;

namespace DocuChat.Application.Validators.Auth;

public class ChangePasswordRequestDtoValidator : AbstractValidator<ChangePasswordRequestDto>
{
    public ChangePasswordRequestDtoValidator()
    {
        RuleFor(x => x.CurrentPassword)
            .NotEmpty().WithMessage("Mevcut şifre boş olamaz.");

        RuleFor(x => x.NewPassword)
            .NotEmpty().WithMessage("Şifre boş olamaz.")
            .MinimumLength(8).WithMessage("Şifre en az 8 karakter olmalıdır.")
            .Matches("[A-ZÇĞİÖŞÜ]").WithMessage("Şifre en az bir büyük harf içermelidir.")
            .Matches("[a-zçğıöşü]").WithMessage("Şifre en az bir küçük harf içermelidir.")
            .Matches("[0-9]").WithMessage("Şifre en az bir rakam içermelidir.")
            .Matches("[^a-zA-ZÇĞİÖŞÜçğıöşü0-9]").WithMessage("Şifre en az bir özel karakter içermelidir.");

        // Yeni şifre mevcut şifre ile aynı olmamalı
        RuleFor(x => x.NewPassword)
            .NotEqual(x => x.CurrentPassword)
            .WithMessage("Yeni şifre mevcut şifreden farklı olmalıdır.");
    }
}
