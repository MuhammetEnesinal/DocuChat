using FluentValidation;
using DocuChat.Application.DTOs.Chat;

namespace DocuChat.Application.Validators.Chat;

public class FeedbackRequestDtoValidator : AbstractValidator<FeedbackRequestDto>
{
    // Whitelist — frontend tarafından gönderilebilecek sebep kategorileri
    public static readonly HashSet<string> AllowedCategories = new(StringComparer.Ordinal)
    {
        "wrong_info",      // Yanlış bilgi
        "missing_info",    // Eksik bilgi
        "nonsense",        // Anlamsız cevap
        "doc_mismatch",    // Belgeyle uyuşmuyor
        "image_issue",     // Görsel yanlış / eksik
    };

    public FeedbackRequestDtoValidator()
    {
        RuleFor(x => x.MessageId)
            .NotEmpty().WithMessage("Mesaj kimliği gereklidir.");

        RuleFor(x => x.Rating)
            .Must(r => r == 1 || r == -1)
            .WithMessage("Geçersiz değerlendirme. +1 veya -1 olmalı.");

        RuleFor(x => x.Categories)
            .Must(cats => cats == null || cats.All(c => AllowedCategories.Contains(c)))
            .WithMessage("Geçersiz sebep kategorisi.");

        RuleFor(x => x.ReasonText)
            .MaximumLength(500).WithMessage("Açıklama en fazla 500 karakter olabilir.");
    }
}
