using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Util;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DocuChat.Infrastructure.Services.Storage;

// S3/MinIO bucket'ı startup'ta yoksa oluşturur. Docker'da MinIO servisi API'den biraz sonra hazır
// olabileceğinden kısa retry yapar. Başarısız olursa uygulamayı DÜŞÜRMEZ (loglar) — ilk upload'a
// kadar MinIO hazırsa sorun olmaz.
public class S3BucketInitializer : IHostedService
{
    private readonly IAmazonS3 _s3;
    private readonly string _bucket;
    private readonly ILogger<S3BucketInitializer> _logger;

    public S3BucketInitializer(IAmazonS3 s3, IConfiguration cfg, ILogger<S3BucketInitializer> logger)
    {
        _s3 = s3;
        _bucket = cfg["Storage:S3:Bucket"] ?? "docuchat";
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken ct)
    {
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            try
            {
                var exists = await AmazonS3Util.DoesS3BucketExistV2Async(_s3, _bucket);
                if (!exists)
                {
                    await _s3.PutBucketAsync(new PutBucketRequest { BucketName = _bucket }, ct);
                    _logger.LogInformation("[S3] Bucket oluşturuldu: {Bucket}", _bucket);
                }
                else
                {
                    _logger.LogInformation("[S3] Bucket mevcut: {Bucket}", _bucket);
                }
                return;
            }
            catch (Exception ex) when (attempt < 5)
            {
                _logger.LogWarning("[S3] Bucket init deneme {Attempt}/5 başarısız ({Msg}) — 2 sn sonra tekrar",
                    attempt, ex.Message);
                await Task.Delay(TimeSpan.FromSeconds(2), ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[S3] Bucket init başarısız: {Bucket} — MinIO hazır olduğunda düzelir", _bucket);
            }
        }
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
