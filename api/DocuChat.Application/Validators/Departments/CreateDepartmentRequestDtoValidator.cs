using FluentValidation;
using DocuChat.Application.DTOs.Departments;

namespace DocuChat.Application.Validators.Departments;

public class CreateDepartmentRequestDtoValidator : AbstractValidator<CreateDepartmentRequestDto>
{
    public CreateDepartmentRequestDtoValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Departman adı boş olamaz.")
            .MaximumLength(150).WithMessage("Departman adı en fazla 150 karakter olabilir.");

        // Kod: kısa tanımlayıcı — boşluk yok, harf/rakam/-/_ serbest (örn. "YAZILIM", "IK-1").
        RuleFor(x => x.Code)
            .NotEmpty().WithMessage("Departman kodu boş olamaz.")
            .MaximumLength(20).WithMessage("Departman kodu en fazla 20 karakter olabilir.")
            .Matches(@"^[A-Za-z0-9ÇĞİıÖŞÜçğöşü_-]+$")
            .WithMessage("Departman kodu boşluk veya özel karakter içeremez (harf, rakam, - ve _ serbest).");
    }
}
