using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using DocuChat.Application.Interfaces.UseCases;

namespace DocuChat.Infrastructure.Services.BackgroundJobs.DocumentProcessing;

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

    // In-flight task tracker — StopAsync sırasında graceful drain için
    private readonly ConcurrentDictionary<Guid, Task> _inflight = new();
    private static readonly TimeSpan GracefulShutdownTimeout = TimeSpan.FromSeconds(30);

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

                // Belge ID için fire-and-forget process — finally'de semaphore release.
                // KRİTİK: Task.Run scheduling kendisi exception fırlatabilir (ThreadPool starvation,
                // OOM). O senaryoda inner finally hiç çalışmaz → semaphore leak → consumer deadlock.
                // Outer try/catch ile scheduling fail durumunda da release garantili.
                try
                {
                    var task = Task.Run(async () =>
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
                            _inflight.TryRemove(documentId, out _);
                        }
                    }, stoppingToken);

                    _inflight[documentId] = task;
                }
                catch (Exception schedEx)
                {
                    // Task scheduling fail oldu — semaphore'u inline release et + log
                    _concurrency.Release();
                    _logger.LogError(schedEx,
                        "[DocConsumer] {DocId} Task scheduling başarısız (semaphore release edildi)",
                        documentId);
                }
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

    /// <summary>
    /// Graceful shutdown — kuyruktan okuma durur, in-flight process'lerin bitmesi (veya 30s timeout)
    /// beklenir. Aksi halde Task.Run'lar abrupt kesilir, belge "Processing" statusta kalır,
    /// next restart'ta recovery service tarafından tekrar baştan parse edilir (Mistral tekrar fatura).
    /// </summary>
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("[DocConsumer] StopAsync — graceful shutdown başlatılıyor (in-flight: {N})", _inflight.Count);

        // Önce consumer loop'u durdur (base class stoppingToken'ı tetikler)
        await base.StopAsync(cancellationToken);

        // In-flight task'lar için drainage. 30s veya cancellationToken (host shutdown timeout) hangisi önce gelirse.
        var pending = _inflight.Values.ToArray();
        if (pending.Length == 0)
        {
            _logger.LogInformation("[DocConsumer] In-flight task yok — temiz çıkış");
            return;
        }

        using var drainCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        drainCts.CancelAfter(GracefulShutdownTimeout);

        try
        {
            var whenAll = Task.WhenAll(pending);
            var winner = await Task.WhenAny(whenAll, Task.Delay(Timeout.Infinite, drainCts.Token));
            if (winner == whenAll)
            {
                _logger.LogInformation("[DocConsumer] {N} in-flight task graceful tamamlandı", pending.Length);
            }
            else
            {
                _logger.LogWarning(
                    "[DocConsumer] Shutdown timeout ({Sec}s) — {Remaining}/{Total} task hala devam ediyor, abrupt kapanıyor",
                    GracefulShutdownTimeout.TotalSeconds, _inflight.Count, pending.Length);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("[DocConsumer] Drain iptal edildi (host shutdown timeout)");
        }
    }
}
