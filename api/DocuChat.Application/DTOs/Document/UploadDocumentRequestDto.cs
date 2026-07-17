namespace DocuChat.Application.DTOs.Document;

public class UploadDocumentRequestDto
{
    public string FileName { get; set; }
    public string ContentType { get; set; }
    public long FileSizeBytes { get; set; }
    public Stream FileStream { get; set; }

    // Belgenin yükleneceği departman (zorunlu). Yönetici yalnız atandığı departmana yükleyebilir.
    public Guid DepartmentId { get; set; }

    public UploadDocumentRequestDto(
        string FileName, string ContentType, long FileSizeBytes, Stream FileStream, Guid DepartmentId)
    {
        this.FileName = FileName;
        this.ContentType = ContentType;
        this.FileSizeBytes = FileSizeBytes;
        this.FileStream = FileStream;
        this.DepartmentId = DepartmentId;
    }
}
