namespace DocuChat.Domain.Exceptions;

public class SessionNotFoundException : DomainException
{
    public SessionNotFoundException(Guid sessionId)
        : base("SESSION_NOT_FOUND", $"Sohbet oturumu bulunamadı. Id: {sessionId}")
    { }
}