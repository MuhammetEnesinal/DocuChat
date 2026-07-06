namespace DocuChat.Application.Interfaces.Services.Documents;

// Belge processing'ini queue'ya ekleyen scheduler — DocumentUseCase ve recovery
// service tarafından çağrılır. Implementation Infrastructure'da (Channel tabanlı
// bounded queue + IHostedService consumer).
// Persistence: DB Status=Pending → recovery hook tarafından yeniden enqueue edilir.
// Bu interface sadece "schedule" abstraction'ı; persistence DB'de Document.Status.
public interface IDocumentProcessingScheduler
{
    // Belge ID'sini processing queue'sune ekle. Kuyruk doluysa bekler (backpressure).
    // Consumer paralel max N belge işler (SemaphoreSlim).
    ValueTask ScheduleAsync(Guid documentId, CancellationToken ct = default);

    // Timeout-aware enqueue: kuyruk doluysa max <paramref name="timeout"/> bekler. Süre
    // dolarsa false döner (caller HTTP 503 / "şu an yoğunluk var" cevabı verebilir, browser
    // timeout'a bırakılmaz). UploadAsync gibi user-facing path'ler için.
    Task<bool> TryScheduleAsync(Guid documentId, TimeSpan timeout, CancellationToken ct = default);
}
