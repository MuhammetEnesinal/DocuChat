namespace DocuChat.Domain.Exceptions;

public class SessionNotFoundException : DomainException
{
    public SessionNotFoundException(Guid id)
        : base("SESSION_NOT_FOUND", $"Oturum bulunamadı. Id: {id}") { }
}