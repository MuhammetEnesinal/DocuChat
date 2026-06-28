using FluentValidation;
using DocuChat.Application.DTOs.Chat;

namespace DocuChat.Application.Validators.Chat;

public class RenameSessionRequestDtoValidator : AbstractValidator<RenameSessionRequestDto>
{
    public RenameSessionRequestDtoValidator()
    {
        RuleFor(x => x.Title)
            .NotEmpty().WithMessage("Oturum adı boş olamaz.")
            .MaximumLength(60).WithMessage("Oturum adı en fazla 60 karakter olabilir.");
    }
}
