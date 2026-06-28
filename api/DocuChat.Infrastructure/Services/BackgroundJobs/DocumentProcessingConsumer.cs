using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using DocuChat.Application.Interfaces.UseCases;

namespace DocuChat.Infrastructure.Services.BackgroundJobs;

/// <summary>
/// DocumentProcessingQueue'dan belge ID'lerini okuyup background processing yapan
/// IHostedService. SemaphoreSlim ile MAKSIMUM N belge eşzamanlı işlenir → resource
/// exhaustion'a karşı koruma (Mistral OCR, BGE-M3, Pixtral hepsi paralel resource'lar).
///
/// Önceden Task.Run ile fire-and-forget vardı — istek ne kadar gelirse hepsi paralel
/// başlardı. Şimdi bounded concurrency: N=2-3 (config'den) → predictable yük.
///
/// Persistence: DocumentRecoveryService startup'ta Pending/Processing belgeleri
/// kuyruğa atar → app restart sonrası işler kaybolmaz.
/// </summary>
public sealed class DocumentProcessingConsumer : BackgroundService
{
    private readonly DocumentProcessingQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DocumentProcessingConsumer> _logger;
    private readonly SemaphoreSlim _concurrency;
    private readonly int _maxConcurrent;

    public DocumentProcessingConsumer(
        DocumentProcessingQueue queue,
        IServiceScopeFactory scopeFactory,
        ILogger<DocumentProcessingConsumer> logger,
        int maxConcurrent = 2)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _logger = logger;
        _maxConcurrent = Math.Max(1, maxConcurrent);
        _concurrency = new SemaphoreSlim(_maxConcurrent, _maxConcurrent);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "[DocConsumer] Servis başlatıldı — maxConcurrent={Max}", _maxConcurrent);

        try
        {
            await foreach (var documentId in _queue.ReadAllAsync(stoppingToken))
            {
                if (stoppingToken.IsCancellationRequested) break;

                // Concurrency limit'i bekle — N belge zaten işleniyor olabilir
                await _concurrency.WaitAsync(stoppingToken);

                // Belge ID için fire-and-forget process — finally'de semaphore release
                _ = Task.Run(async () =>
                {
                    try
                    {
                        using var scope = _scopeFactory.CreateScope();
                        var useCase = scope.ServiceProvider.GetRequiredService<IDocumentUseCase>();
                        await useCase.ProcessPendingAsync(documentId, stoppingToken);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        _logger.LogWarning("[DocConsumer] {DocId} kapanırken iptal", documentId);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "[DocConsumer] {DocId} process hatası", documentId);
                    }
                    finally
                    {
                        _concurrency.Release();
                    }
                }, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("[DocConsumer] Kuyruktan okuma sonlandı (app shutdown)");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[DocConsumer] Beklenmedik hata — consumer durdu");
        }
    }
}
