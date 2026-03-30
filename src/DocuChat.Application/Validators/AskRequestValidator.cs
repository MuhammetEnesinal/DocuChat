using DocuChat.Application.DTOs.Chat;
using FluentValidation;

namespace DocuChat.Application.Validators;

public class AskRequestValidator : AbstractValidator<AskRequestDto>
{
    public AskRequestValidator()
    {
        RuleFor(x => x.DocumentId)
            .NotEmpty().WithMessage("Belge seçilmeden soru sorulamaz.");

        RuleFor(x => x.Question)
            .NotEmpty().WithMessage("Soru boş olamaz.")
            .MinimumLength(3).WithMessage("Soru en az 3 karakter olmalıdır.")
            .MaximumLength(2000).WithMessage("Soru 2000 karakteri geçemez.");
    }
}