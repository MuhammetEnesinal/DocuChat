namespace DocuChat.Application.Interfaces.Services;

/// <summary>
/// Belge processing'ini queue'ya ekleyen scheduler — DocumentUseCase ve recovery
/// service tarafından çağrılır. Implementation Infrastructure'da (Channel tabanlı
/// bounded queue + IHostedService consumer).
///
/// Persistence: DB Status=Pending → recovery hook tarafından yeniden enqueue edilir.
/// Bu interface sadece "schedule" abstraction'ı; persistence DB'de Document.Status.
/// </summary>
public interface IDocumentProcessingScheduler
{
    /// <summary>
    /// Belge ID'sini processing queue'sune ekle. Kuyruk doluysa bekler (backpressure).
    /// Consumer paralel max N belge işler (SemaphoreSlim).
    /// </summary>
    ValueTask ScheduleAsync(Guid documentId, CancellationToken ct = default);
}
