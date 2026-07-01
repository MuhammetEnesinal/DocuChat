namespace DocuChat.Application.DTOs.Document;

public class UploadDocumentRequestDto
{
    public string FileName { get; set; }
    public string ContentType { get; set; }
    public long FileSizeBytes { get; set; }
    public Stream FileStream { get; set; }

    public UploadDocumentRequestDto(string FileName, string ContentType, long FileSizeBytes, Stream FileStream)
    {
        this.FileName = FileName;
        this.ContentType = ContentType;
        this.FileSizeBytes = FileSizeBytes;
        this.FileStream = FileStream;
    }
}
