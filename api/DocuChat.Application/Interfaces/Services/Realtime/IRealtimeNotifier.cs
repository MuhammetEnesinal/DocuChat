namespace DocuChat.Application.Interfaces.Services.Realtime;

/// <summary>
/// Gerçek zamanlı "sinyal" yayınlayıcısı. Desen: veriyi DEĞİL, "şu değişti" sinyalini taşır
/// (signal, don't send state). İstemci sinyali alınca mevcut REST fetch'ini tekrar çalıştırır.
/// Böylece filtre/sayfalama/yetki/projeksiyon mantığı tek yerde (REST) kalır.
///
/// Implementasyon API katmanındadır (SignalRNotifier + IHubContext). Use-case'ler yalnız bu
/// arayüze bağımlıdır; SignalR sızıntısı olmaz. Bildirim gönderimi "best-effort"tur: hata
/// fırlatmamalı, çünkü bir kullanıcının kopuk soketi asıl iş akışını (kayıt, silme) bozmamalı.
/// </summary>
public interface IRealtimeNotifier
{
    /// <summary>Tek kullanıcının tüm bağlantılarına (çoklu sekme/cihaz) yollar. Grup: user:{userId}.</summary>
    Task NotifyUserAsync(string userId, string eventType, object? payload = null, CancellationToken ct = default);

    /// <summary>Bir departmanın üyelerine yollar. Grup: dept:{departmentId}. Departman izolasyonunu korur.</summary>
    Task NotifyDepartmentAsync(Guid departmentId, string eventType, object? payload = null, CancellationToken ct = default);

    /// <summary>Tüm admin bağlantılarına yollar (yönetim ekranı canlılığı). Grup: admins.</summary>
    Task NotifyAdminsAsync(string eventType, object? payload = null, CancellationToken ct = default);
}
