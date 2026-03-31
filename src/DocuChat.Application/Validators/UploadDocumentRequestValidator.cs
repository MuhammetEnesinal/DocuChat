using FluentValidation;
using DocuChat.Application.DTOs.Document;

namespace DocuChat.Application.Validators;

public class UploadDocumentRequestValidator : AbstractValidator<UploadDocumentRequest>
{
    private static readonly string[] AllowedTypes =
    {
        "application/pdf",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        "text/plain",
        "text/csv"
    };

    public UploadDocumentRequestValidator()
    {
        RuleFor(x => x.FileName)
            .NotEmpty().WithMessage("Dosya adı boş olamaz.")
            .MaximumLength(255).WithMessage("Dosya adı en fazla 255 karakter olabilir.");

        RuleFor(x => x.ContentType)
            .Must(t => AllowedTypes.Contains(t))
            .WithMessage("Desteklenen formatlar: PDF, DOCX, XLSX, TXT, CSV.");

        RuleFor(x => x.FileSizeBytes)
            .InclusiveBetween(1, 50 * 1024 * 1024)
            .WithMessage("Dosya boyutu 1 bayt ile 50 MB arasında olmalıdır.");
    }
}