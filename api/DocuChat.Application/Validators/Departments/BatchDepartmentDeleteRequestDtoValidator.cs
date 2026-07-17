using FluentValidation;
using DocuChat.Application.DTOs.Departments;

namespace DocuChat.Application.Validators.Departments;

public class BatchDepartmentDeleteRequestDtoValidator : AbstractValidator<BatchDepartmentDeleteRequestDto>
{
    public BatchDepartmentDeleteRequestDtoValidator()
    {
        RuleFor(x => x.Ids)
            .NotNull().WithMessage("Silinecek departman ID listesi boş olamaz.")
            .Must(ids => ids.Any()).WithMessage("En az bir departman ID belirtilmelidir.")
            .Must(ids => ids.Count() <= 100).WithMessage("Tek seferde en fazla 100 departman silinebilir.");
    }
}
