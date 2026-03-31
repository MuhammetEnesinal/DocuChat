namespace DocuChat.Application.DTOs.Document;

public record UploadDocumentRequest(
    string FileName,
    string ContentType,
    long FileSizeBytes,
    Stream FileStream);