using FluentValidation;
using DocuChat.Application.DTOs.Document;

namespace DocuChat.Application.Validators.Document;

public class BatchDocumentDeleteRequestValidator : AbstractValidator<BatchDocumentDeleteRequest>
{
    public BatchDocumentDeleteRequestValidator()
    {
        RuleFor(x => x.Ids)
            .NotNull().WithMessage("Silinecek belge ID listesi boş olamaz.")
            .Must(ids => ids.Any()).WithMessage("En az bir belge ID belirtilmelidir.")
            .Must(ids => ids.Count() <= 100).WithMessage("Tek seferde en fazla 100 belge silinebilir.");
    }
}
