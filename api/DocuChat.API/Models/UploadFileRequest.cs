namespace DocuChat.API.Models;

public class UploadFileRequest
{
    public IFormFile File { get; set; } = null!;
}
