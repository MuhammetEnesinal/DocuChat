using System.Threading.Channels;
using DocuChat.Application.Interfaces.Services;

namespace DocuChat.Infrastructure.Services.BackgroundJobs;

/// <summary>
/// In-memory bounded channel queue — belge ID'lerini DocumentProcessingConsumer'ın
/// işlemesi için sıraya alır. DocumentUseCase.UploadAsync ve DocumentRecoveryService
/// burayı kullanır.
///
/// Persistence DocumentRecoveryService'te → app restart sonrası Pending/Processing
/// statüsündeki belgeler tekrar enqueue edilir. Channel sadece concurrency kontrolü ve
/// backpressure için.
///
/// Bounded capacity → "Wait" mode: kuyruk doluysa enqueue producer'ı bekletir.
/// </summary>
public sealed class DocumentProcessingQueue : IDocumentProcessingScheduler
{
    private readonly Channel<Guid> _channel;

    public DocumentProcessingQueue(int capacity = 1000)
    {
        _channel = Channel.CreateBounded<Guid>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = false
        });
    }

    /// <summary>IDocumentProcessingScheduler implementation.</summary>
    public ValueTask ScheduleAsync(Guid documentId, CancellationToken ct = default) =>
        _channel.Writer.WriteAsync(documentId, ct);

    /// <summary>Consumer için stream — döngüde await foreach ile okunur.</summary>
    public IAsyncEnumerable<Guid> ReadAllAsync(CancellationToken ct) =>
        _channel.Reader.ReadAllAsync(ct);

    /// <summary>Kuyruğu kapat (app shutdown). Pending item'lar consumer'da işlenir.</summary>
    public void Complete() => _channel.Writer.TryComplete();
}
