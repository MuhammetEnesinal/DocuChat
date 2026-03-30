using DocuChat.Application.DTOs.Document;
using FluentValidation;

namespace DocuChat.Application.Validators;

public class UploadDocumentRequestValidator : AbstractValidator<UploadDocumentRequestDto>
{
    private static readonly string[] AllowedTypes =
    [
        "application/pdf",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        "text/plain",
        "text/csv"
    ];

    public UploadDocumentRequestValidator()
    {
        RuleFor(x => x.FileName)
            .NotEmpty().WithMessage("Dosya adı boş olamaz.");

        RuleFor(x => x.FileSizeBytes)
            .GreaterThan(0).WithMessage("Dosya boş olamaz.")
            .LessThanOrEqualTo(50 * 1024 * 1024).WithMessage("Dosya 50 MB'ı geçemez.");

        RuleFor(x => x.ContentType)
            .NotEmpty().WithMessage("Dosya tipi boş olamaz.")
            .Must(ct => AllowedTypes.Contains(ct))
            .WithMessage("Desteklenmeyen dosya tipi. PDF, DOCX, XLSX, TXT veya CSV yükleyiniz.");
    }
}