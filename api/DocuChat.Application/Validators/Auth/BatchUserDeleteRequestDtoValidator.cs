using FluentValidation;
using DocuChat.Application.DTOs.Auth;

namespace DocuChat.Application.Validators.Auth;

public class BatchUserDeleteRequestDtoValidator : AbstractValidator<BatchUserDeleteRequestDto>
{
    public BatchUserDeleteRequestDtoValidator()
    {
        RuleFor(x => x.Ids)
            .NotNull().WithMessage("Silinecek kullanıcı ID listesi boş olamaz.")
            .Must(ids => ids.Any()).WithMessage("En az bir kullanıcı ID belirtilmelidir.")
            .Must(ids => ids.Count() <= 100).WithMessage("Tek seferde en fazla 100 kullanıcı silinebilir.")
            .Must(ids => ids.All(id => !string.IsNullOrWhiteSpace(id))).WithMessage("Boş ID kabul edilmez.");
    }
}
