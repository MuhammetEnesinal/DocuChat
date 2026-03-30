namespace DocuChat.Application.DTOs.Document;

public record UploadDocumentRequestDto(
    string FileName,
    string ContentType,
    long FileSizeBytes,
    Stream FileStream);