namespace DocuChat.API.Common;

public class UploadFileRequest
{
    public IFormFile File { get; set; } = null!;

    // Belgenin yükleneceği departman (multipart form alanı). Zorunlu.
    public Guid DepartmentId { get; set; }
}
