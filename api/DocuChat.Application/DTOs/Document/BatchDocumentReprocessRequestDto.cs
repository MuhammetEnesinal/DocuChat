namespace DocuChat.Application.DTOs.Document;

public class BatchDocumentReprocessRequestDto
{
    public IEnumerable<Guid> Ids { get; set; }

    public BatchDocumentReprocessRequestDto(IEnumerable<Guid> Ids)
    {
        this.Ids = Ids;
    }
}
