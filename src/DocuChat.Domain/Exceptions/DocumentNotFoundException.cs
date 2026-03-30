namespace DocuChat.Domain.Exceptions;

public class DocumentNotFoundException : DomainException
{
    public DocumentNotFoundException(Guid documentId)
        : base("DOCUMENT_NOT_FOUND", $"Belge bulunamadı. Id: {documentId}")
    { }
}