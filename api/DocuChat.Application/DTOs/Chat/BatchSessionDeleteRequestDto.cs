namespace DocuChat.Application.DTOs.Chat;

public class BatchSessionDeleteRequestDto
{
    public IEnumerable<Guid> Ids { get; set; }

    public BatchSessionDeleteRequestDto(IEnumerable<Guid> Ids)
    {
        this.Ids = Ids;
    }
}
