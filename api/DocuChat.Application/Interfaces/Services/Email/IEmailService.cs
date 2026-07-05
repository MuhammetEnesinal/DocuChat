namespace DocuChat.Application.Interfaces.Services.Email;

public interface IEmailService
{
    Task SendAsync(string to, string subject, string htmlBody, CancellationToken ct = default);
}
