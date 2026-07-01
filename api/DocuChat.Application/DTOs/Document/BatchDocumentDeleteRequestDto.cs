namespace DocuChat.Application.DTOs.Document;

public class BatchDocumentDeleteRequestDto
{
    public IEnumerable<Guid> Ids { get; set; }

    public BatchDocumentDeleteRequestDto(IEnumerable<Guid> Ids)
    {
        this.Ids = Ids;
    }
}
