// DocuChat.Infrastructure/Services/CacheCleanupService.cs
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using DocuChat.Application.Interfaces.Repositories;

namespace DocuChat.Infrastructure.Services.BackgroundJobs;

public sealed class CacheCleanupService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CacheCleanupService> _logger;

    private static readonly TimeSpan CleanupInterval = TimeSpan.FromHours(1);
    private static readonly TimeSpan CacheTtl = TimeSpan.FromDays(30);

    public CacheCleanupService(IServiceScopeFactory scopeFactory, ILogger<CacheCleanupService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[CacheCleanup] Servis başlatıldı — her {Interval} saatte çalışacak", CleanupInterval.TotalHours);

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(CleanupInterval, stoppingToken);

            if (stoppingToken.IsCancellationRequested) break;

            await RunCleanupAsync(stoppingToken);
        }
    }

    private async Task RunCleanupAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        try
        {
            var deleted = await uow.QuestionCache.DeleteExpiredAsync(CacheTtl, ct);
            if (deleted > 0)
                _logger.LogInformation("[CacheCleanup] {Count} eski kayıt silindi", deleted);
            else
                _logger.LogDebug("[CacheCleanup] Silinecek kayıt yok");
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "[CacheCleanup] Temizleme sırasında hata");
        }
    }
}
