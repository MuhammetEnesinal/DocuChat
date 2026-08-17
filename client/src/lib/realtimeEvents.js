// Gerçek zamanlı event adları — backend RealtimeEventTypes.cs ile birebir aynı olmalı.
// Sinyal payload'ı minik tutulur ({ type, payload:{ id } }); veri REST'ten çekilir.
export const RealtimeEvents = {
    ChatMessageAdded: 'chat.message.added',   // payload: { sessionId } — sohbete yeni mesaj
    ChatSessionChanged: 'chat.session.changed', // sohbet oluştu/adı/arşiv/pin/silme değişti
    DocumentChanged: 'document.changed',       // payload: { documentId, status } — dept kapsamlı
    UserChanged: 'user.changed',               // payload: { userId } — yönetim ekranı
    UserRefresh: 'user.refresh',               // İLGİLİ kullanıcıya: token'ını sessizce tazele (dept/rol — yumuşak)
    SessionTerminated: 'session.terminated',   // İLGİLİ kullanıcıya: TÜM cihazlarda çıkış (şifre/e-posta/silme — sert)
    DepartmentChanged: 'department.changed',   // departman CRUD — yönetim ekranı
};
