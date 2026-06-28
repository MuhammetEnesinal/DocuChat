using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using DocuChat.Application.Interfaces.Repositories;
using DocuChat.Application.Interfaces.Services;
using DocuChat.Application.Interfaces.UseCases;
using DocuChat.Domain.Enums;

namespace DocuChat.Infrastructure.Services.BackgroundJobs;

/// <summary>
/// Uygulama başlarken Pending veya Processing statüsünde KALMIŞ belgeleri tekrar
/// background processing'e atar. Bir önceki run sırasında uygulamanın crash etmesi,
/// deploy edilmesi veya manuel restart olması durumunda işin kaybolmasını önler.
///
/// AKIŞ:
///   1. IHostedService olarak uygulama startup'ında ÇALIŞIR (StartAsync)
///   2. DocumentRepository ile Pending+Processing statüsündeki belge ID'lerini çek
///   3. Her bir ID için DocumentUseCase.ProcessPendingAsync'i background Task.Run ile başlat
///   4. Service yaşamı boyunca tek seferlik çalışır, sonra idle olur
///
/// NOT: Burası "minimum viable" recovery. Gelecekte Hangfire / Quartz / DB-backed queue
/// ile değiştirilebilir. Şimdilik in-memory ama startup ile birlikte tekrar denenir.
/// </summary>
public sealed class DocumentRecoveryService : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ILogger<DocumentRecoveryService> _logger;

    public DocumentRecoveryService(
        IServiceScopeFactory scopeFactory,
        IHostApplicationLifetime lifetime,
        ILogger<DocumentRecoveryService> logger)
    {
        _scopeFactory = scopeFactory;
        _lifetime = lifetime;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Application başlaması ile recovery işini fire-and-forget başlat — startup'ı bloklamasın.
        _ = Task.Run(async () =>
        {
            try
            {
                await RecoverPendingDocumentsAsync(_lifetime.ApplicationStopping);
            }
            catch (OperationCanceledException) when (_lifetime.ApplicationStopping.IsCancellationRequested)
            {
                _logger.LogInformation("[DocRecovery] Recovery uygulama kapanırken iptal edildi");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[DocRecovery] Beklenmedik hata — recovery atlandı");
            }
        });
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task RecoverPendingDocumentsAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var scheduler = scope.ServiceProvider.GetRequiredService<IDocumentProcessingScheduler>();

        var statuses = new[] { DocumentStatus.Pending, DocumentStatus.Processing };
        var ids = await uow.Documents.GetIdsByStatusAsync(statuses, ct);
        if (ids.Count == 0)
        {
            _logger.LogInformation("[DocRecovery] Recover edilecek belge yok");
            return;
        }

        _logger.LogWarning(
            "[DocRecovery] Önceki run'da yarıda kalmış {Count} belge tespit edildi — kuyruğa ekleniyor",
            ids.Count);

        // 🆕 Doğrudan Task.Run ile process etmek YERİNE queue'ya enqueue et —
        // DocumentProcessingConsumer bounded concurrency ile işler.
        foreach (var docId in ids)
        {
            if (ct.IsCancellationRequested) break;
            await scheduler.ScheduleAsync(docId, ct);
        }
        _logger.LogInformation("[DocRecovery] {Count} belge processing queue'sune eklendi", ids.Count);
    }
}
