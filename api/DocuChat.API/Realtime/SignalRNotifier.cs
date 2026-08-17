using Microsoft.AspNetCore.SignalR;
using DocuChat.Application.Interfaces.Services.Realtime;

namespace DocuChat.API.Realtime;

/// <summary>
/// IRealtimeNotifier'ın SignalR implementasyonu. Use-case'ler IHubContext'i doğrudan görmesin
/// diye araya girer. Gönderim best-effort: hub gönderimi hata verirse (kopuk soket, kapanan
/// bağlantı) yutulur ve loglanır — bir bildirimin başarısızlığı asıl iş akışını bozmamalı.
/// Grup adları NotificationHub ile tek kaynaktan üretilir (drift olmaz).
/// </summary>
public class SignalRNotifier : IRealtimeNotifier
{
    // İstemci tarafında dinlenen tek event adı. Frontend: connection.on("event", ...).
    private const string ClientMethod = "event";

    private readonly IHubContext<NotificationHub> _hub;
    private readonly ILogger<SignalRNotifier> _logger;

    public SignalRNotifier(IHubContext<NotificationHub> hub, ILogger<SignalRNotifier> logger)
    {
        _hub = hub;
        _logger = logger;
    }

    public Task NotifyUserAsync(string userId, string eventType, object? payload = null, CancellationToken ct = default)
        => SendAsync(NotificationHub.UserGroup(userId), eventType, payload, ct);

    public Task NotifyDepartmentAsync(Guid departmentId, string eventType, object? payload = null, CancellationToken ct = default)
        => SendAsync(NotificationHub.DepartmentGroup(departmentId), eventType, payload, ct);

    public Task NotifyAdminsAsync(string eventType, object? payload = null, CancellationToken ct = default)
        => SendAsync(NotificationHub.AdminsGroup, eventType, payload, ct);

    private async Task SendAsync(string group, string eventType, object? payload, CancellationToken ct)
    {
        try
        {
            await _hub.Clients.Group(group).SendAsync(ClientMethod, new { type = eventType, payload }, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Realtime bildirim gönderilemedi. Group={Group} Event={Event}", group, eventType);
        }
    }
}
