namespace DocuChat.Application.DTOs.Auth;

// Toplu kullanıcı import — tek bir satırın işlem sonucu.
// Status: "success" | "skipped"
public class BulkImportUserResultDto
{
    public int Row { get; set; }             // Excel satır numarası (1-bazlı, header sayılmaz → ilk veri satırı = 2)
    public string? Email { get; set; }       // Satırdaki email (boş satırlar veya format yanlışsa null)
    public string Status { get; set; }       // "success" | "skipped"
    public string? Reason { get; set; }      // Skipped olduysa sebep (validation hatası, email zaten kayıtlı, vb.)

    public BulkImportUserResultDto(int Row, string? Email, string Status, string? Reason)
    {
        this.Row = Row;
        this.Email = Email;
        this.Status = Status;
        this.Reason = Reason;
    }
}

// Toplu import — tüm dosyanın özet sonucu.
// Per-row results admin UI'da tabloda gösterilir.
public class BulkImportUsersSummaryDto
{
    public int TotalRows { get; set; }
    public int SuccessCount { get; set; }
    public int SkippedCount { get; set; }
    public IReadOnlyList<BulkImportUserResultDto> Results { get; set; }

    public BulkImportUsersSummaryDto(
        int TotalRows, int SuccessCount, int SkippedCount, IReadOnlyList<BulkImportUserResultDto> Results)
    {
        this.TotalRows = TotalRows;
        this.SuccessCount = SuccessCount;
        this.SkippedCount = SkippedCount;
        this.Results = Results;
    }
}
