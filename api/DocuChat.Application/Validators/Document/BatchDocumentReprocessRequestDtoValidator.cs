using FluentValidation;
using DocuChat.Application.DTOs.Document;

namespace DocuChat.Application.Validators.Document;

public class BatchDocumentReprocessRequestDtoValidator : AbstractValidator<BatchDocumentReprocessRequestDto>
{
    public BatchDocumentReprocessRequestDtoValidator()
    {
        RuleFor(x => x.Ids)
            .NotNull().WithMessage("Yeniden işlenecek belge ID listesi boş olamaz.")
            .Must(ids => ids.Any()).WithMessage("En az bir belge ID belirtilmelidir.")
            .Must(ids => ids.Count() <= 100).WithMessage("Tek seferde en fazla 100 belge yeniden işlenebilir.");
    }
}
