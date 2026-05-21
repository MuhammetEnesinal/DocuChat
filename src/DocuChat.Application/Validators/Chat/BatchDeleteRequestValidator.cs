using FluentValidation;
using DocuChat.Application.DTOs.Chat;

namespace DocuChat.Application.Validators.Chat;

public class BatchDeleteRequestValidator : AbstractValidator<BatchDeleteRequest>
{
    public BatchDeleteRequestValidator()
    {
        RuleFor(x => x.Ids)
            .NotNull().WithMessage("Silinecek ID listesi boş olamaz.")
            .Must(ids => ids.Any()).WithMessage("En az bir ID belirtilmelidir.")
            .Must(ids => ids.Count() <= 100).WithMessage("Tek seferde en fazla 100 kayıt silinebilir.");
    }
}
