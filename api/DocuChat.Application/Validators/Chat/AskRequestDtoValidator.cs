using FluentValidation;
using DocuChat.Application.DTOs.Chat;

namespace DocuChat.Application.Validators.Chat;

public class AskRequestDtoValidator : AbstractValidator<AskRequestDto>
{
    public AskRequestDtoValidator()
    {
        RuleFor(x => x.Question)
            .NotEmpty().WithMessage("Soru boş olamaz.")
            .MaximumLength(2000).WithMessage("Soru en fazla 2000 karakter olabilir.");
    }
}
