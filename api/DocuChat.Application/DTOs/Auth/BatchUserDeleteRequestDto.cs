namespace DocuChat.Application.DTOs.Auth;

public class BatchUserDeleteRequestDto
{
    public IEnumerable<string> Ids { get; set; }

    public BatchUserDeleteRequestDto(IEnumerable<string> Ids)
    {
        this.Ids = Ids;
    }
}
