namespace DocuChat.Domain.Exceptions;

public class DocumentNotFoundException : DomainException
{
    public DocumentNotFoundException(Guid id)
        : base("DOCUMENT_NOT_FOUND", $"Belge bulunamadı. Id: {id}") { }
}