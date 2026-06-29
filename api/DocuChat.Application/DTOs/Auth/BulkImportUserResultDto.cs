namespace DocuChat.Application.DTOs.Auth;

/// <summary>
/// Toplu kullanıcı import — tek bir satırın işlem sonucu.
/// Status: "success" | "skipped"
/// </summary>
public record BulkImportUserResultDto(
    int Row,             // Excel satır numarası (1-bazlı, header sayılmaz → ilk veri satırı = 2)
    string? Email,       // Satırdaki email (boş satırlar veya format yanlışsa null)
    string Status,       // "success" | "skipped"
    string? Reason       // Skipped olduysa sebep (validation hatası, email zaten kayıtlı, vb.)
);

/// <summary>
/// Toplu import — tüm dosyanın özet sonucu.
/// Per-row results admin UI'da tabloda gösterilir.
/// </summary>
public record BulkImportUsersSummaryDto(
    int TotalRows,
    int SuccessCount,
    int SkippedCount,
    IReadOnlyList<BulkImportUserResultDto> Results
);
