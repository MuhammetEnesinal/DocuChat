namespace DocuChat.Application.Interfaces.Services.Realtime;

/// <summary>
/// Gerçek zamanlı event adları — frontend realtimeEvents.js ile birebir aynı olmalı.
/// Sinyal payload'ı minik tutulur; asıl veri istemcide REST ile çekilir.
/// </summary>
public static class RealtimeEventTypes
{
    public const string ChatMessageAdded = "chat.message.added";     // payload: { sessionId }
    public const string ChatSessionChanged = "chat.session.changed"; // sohbet oluştu/adı/arşiv/pin/silme
    public const string DocumentChanged = "document.changed";        // payload: { documentId, status } — dept kapsamlı
    public const string UserChanged = "user.changed";                // payload: { userId } — yönetim ekranı
    public const string UserRefresh = "user.refresh";                // İLGİLİ kullanıcıya: token'ını tazele (dept/rol — yumuşak)
    public const string SessionTerminated = "session.terminated";    // İLGİLİ kullanıcıya: TÜM cihazlarda çıkış (şifre/e-posta/silme — sert)
    public const string DepartmentChanged = "department.changed";    // departman CRUD — yönetim ekranı
}
