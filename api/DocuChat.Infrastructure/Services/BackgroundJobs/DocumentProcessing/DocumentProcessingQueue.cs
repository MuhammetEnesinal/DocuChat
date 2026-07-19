using System.Threading.Channels;
using DocuChat.Application.Interfaces.Services.Ai.Embedding;
using DocuChat.Application.Interfaces.Services.Ai.Llm;
using DocuChat.Application.Interfaces.Services.Ai.Reranker;
using DocuChat.Application.Interfaces.Services.Ai.Retrieval;
using DocuChat.Application.Interfaces.Services.Documents;
using DocuChat.Application.Interfaces.Services.Auth;
using DocuChat.Application.Interfaces.Services.UserManagement;
using DocuChat.Application.Interfaces.Services.Email;
using DocuChat.Application.Interfaces.Services.Storage;
using DocuChat.Application.Interfaces.Services.Persistence;

namespace DocuChat.Infrastructure.Services.BackgroundJobs.DocumentProcessing;

// In-memory bounded channel queue — belge ID'lerini DocumentProcessingConsumer'ın
// işlemesi için sıraya alır. DocumentUseCase.UploadAsync ve DocumentRecoveryService
// burayı kullanır.
// Persistence DocumentRecoveryService'te → app restart sonrası Pending/Processing
// statüsündeki belgeler tekrar enqueue edilir. Channel sadece concurrency kontrolü ve
// backpressure için.
// Bounded capacity → "Wait" mode: kuyruk doluysa enqueue producer'ı bekletir.
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

    // IDocumentProcessingScheduler implementation. Recovery service için bloklu.
    public ValueTask ScheduleAsync(Guid documentId, CancellationToken ct = default) =>
        _channel.Writer.WriteAsync(documentId, ct);

    // Timeout-aware enqueue — kuyruk doluysa süresiz bekleme YOK.
    public async Task<bool> TryScheduleAsync(Guid documentId, TimeSpan timeout, CancellationToken ct = default)
    {
        // Önce non-blocking deneme
        if (_channel.Writer.TryWrite(documentId)) return true;

        // Kuyruk dolu → bounded timeout ile bekle
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout);
        try
        {
            await _channel.Writer.WriteAsync(documentId, timeoutCts.Token);
            return true;
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            return false;
        }
    }

    // Consumer için stream — döngüde await foreach ile okunur.
    public IAsyncEnumerable<Guid> ReadAllAsync(CancellationToken ct) =>
        _channel.Reader.ReadAllAsync(ct);

    // Kuyruğa yeni öğe alınmasını durdurur; kuyrukta bekleyenler consumer tarafından işlenmeye
    // devam eder. Uygulama kapanışında kuyruk kapatılmadan da consumer, iptal belirteciyle
    // sonlandığı için bu çağrı zorunlu değildir.
    public void Complete() => _channel.Writer.TryComplete();
}
