using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using DocuChat.Application.Interfaces.Services;

namespace DocuChat.Infrastructure.Services;

public class SmtpEmailService : IEmailService
{
    private readonly string _host;
    private readonly int _port;
    private readonly string _from;
    private readonly string _password;
    private readonly ILogger<SmtpEmailService> _logger;

    public SmtpEmailService(IConfiguration cfg, ILogger<SmtpEmailService> logger)
    {
        _host     = cfg["Email:Host"]     ?? throw new InvalidOperationException("Email:Host config eksik.");
        _port     = int.Parse(cfg["Email:Port"] ?? "587");
        _from     = cfg["Email:From"]     ?? throw new InvalidOperationException("Email:From config eksik.");
        _password = cfg["Email:Password"] ?? throw new InvalidOperationException("Email:Password config eksik.");
        _logger   = logger;
    }

    public async Task SendAsync(string to, string subject, string htmlBody, CancellationToken ct = default)
    {
        using var client = new SmtpClient(_host, _port)
        {
            EnableSsl = true,
            Credentials = new NetworkCredential(_from, _password),
        };

        using var message = new MailMessage(_from, to, subject, htmlBody)
        {
            IsBodyHtml = true,
        };

        try
        {
            await client.SendMailAsync(message, ct);
            _logger.LogInformation("Mail gönderildi: {To} — {Subject}", to, subject);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Mail gönderilemedi: {To} — {Subject}", to, subject);
            throw;
        }
    }
}
