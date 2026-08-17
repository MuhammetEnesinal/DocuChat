using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using DocuChat.Domain.Enums;
using DocuChat.Infrastructure.Services.Auth;

namespace DocuChat.API.Realtime;

/// <summary>
/// Tek gerçek zamanlı hub. Yalnız "sinyal" taşır (payload minik: type + id). Bağlantı açılınca
/// kullanıcı JWT claim'lerinden gruplara katılır; böylece bir kullanıcı YALNIZ kendi verisiyle
/// (kendi kullanıcı grubu, üye olduğu departmanlar, admin ise admins) ilgili sinyalleri alır.
/// Bu grup üyeliği aynı zamanda güvenlik sınırıdır: departman izolasyonu burada da korunur —
/// bir kullanıcı üye olmadığı departmanın grubuna hiç girmediği için o event'leri asla almaz.
///
/// İstemci→sunucu metodu yok; iletişim tek yönlü (server→client "event"). İstemci sadece dinler.
/// </summary>
[Authorize]
public class NotificationHub : Hub
{
    public const string AdminsGroup = "admins";
    public static string UserGroup(string userId) => $"user:{userId}";
    public static string DepartmentGroup(Guid departmentId) => $"dept:{departmentId}";

    private readonly ILogger<NotificationHub> _logger;

    public NotificationHub(ILogger<NotificationHub> logger) => _logger = logger;

    public override async Task OnConnectedAsync()
    {
        var user = Context.User;
        var userId = user?.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (!string.IsNullOrEmpty(userId))
            await Groups.AddToGroupAsync(Context.ConnectionId, UserGroup(userId));

        if (user is not null)
        {
            foreach (var deptClaim in user.FindAll(AppClaimTypes.Department))
                if (Guid.TryParse(deptClaim.Value, out var deptId))
                    await Groups.AddToGroupAsync(Context.ConnectionId, DepartmentGroup(deptId));

            if (user.IsInRole(Roles.Admin))
                await Groups.AddToGroupAsync(Context.ConnectionId, AdminsGroup);
        }

        _logger.LogInformation(
            "Realtime bağlantı açıldı. User={UserId} Conn={ConnId}", userId, Context.ConnectionId);
        await base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation("Realtime bağlantı kapandı. Conn={ConnId}", Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }
}
