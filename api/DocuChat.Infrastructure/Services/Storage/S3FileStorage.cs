using System.Net;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using DocuChat.Application.Interfaces.Services.Storage;

namespace DocuChat.Infrastructure.Services.Storage;

// IFileStorage'ın S3-uyumlu (MinIO veya AWS S3) implementasyonu. storagePath = obje "key";
// alt klasör = key prefix'i (S3'te gerçek klasör yoktur, ayraç yalnız isimlendirmedir).
// Tek IFileStorage implementasyonudur → DocumentUseCase/ChatUseCase somut depoyu bilmez.
public class S3FileStorage : IFileStorage
{
    private readonly IAmazonS3 _s3;
    private readonly string _bucket;
    private readonly ILogger<S3FileStorage> _logger;

    public S3FileStorage(IAmazonS3 s3, IConfiguration cfg, ILogger<S3FileStorage> logger)
    {
        _s3 = s3;
        _bucket = cfg["Storage:S3:Bucket"] ?? "docuchat";
        _logger = logger;
    }

    public Task<string> SaveAsync(Stream stream, string fileName, string? subFolder = null, CancellationToken ct = default)
    {
        var prefix = $"{Guid.NewGuid()}_";
        var key = Combine(subFolder, prefix + Truncate(SanitizeFileName(fileName)));
        return PutAsync(stream, key, ct);
    }

    public Task<string> SaveRawAsync(Stream stream, string exactFileName, string? subFolder = null, CancellationToken ct = default)
    {
        var key = Combine(subFolder, Truncate(SanitizeFileName(exactFileName)));
        return PutAsync(stream, key, ct);
    }

    private async Task<string> PutAsync(Stream stream, string key, CancellationToken ct)
    {
        if (stream.CanSeek) stream.Position = 0;

        // S3 PutObject içerik uzunluğu ister. Seekable değilse (uzunluk bilinemez) belleğe tamponla.
        Stream body = stream;
        MemoryStream? buffer = null;
        if (!stream.CanSeek)
        {
            buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, ct);
            buffer.Position = 0;
            body = buffer;
        }

        try
        {
            await _s3.PutObjectAsync(new PutObjectRequest
            {
                BucketName = _bucket,
                Key = key,
                InputStream = body,
                AutoCloseStream = false,   // stream'i çağıran yönetir
                ContentType = GuessContentType(key),
            }, ct);
        }
        finally
        {
            if (buffer is not null) await buffer.DisposeAsync();
        }
        return key;
    }

    public Stream OpenRead(string storagePath)
    {
        // IFileStorage.OpenRead senkron; S3 async olduğundan burada bloklanır (iç uygulama için kabul).
        // Dönen ResponseStream canlı ağ akışıdır; çağıran dispose eder (RegisterForDispose).
        try
        {
            var resp = _s3.GetObjectAsync(_bucket, storagePath).GetAwaiter().GetResult();
            return resp.ResponseStream;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            throw new FileNotFoundException($"Dosya bulunamadı: {storagePath}");
        }
    }

    public Task DeleteAsync(string storagePath, CancellationToken ct = default)
        => _s3.DeleteObjectAsync(_bucket, storagePath, ct);   // key yoksa DeleteObject no-op'tur

    public async Task DeleteDirectoryAsync(string relativeFolder, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(relativeFolder)) return;

        // S3'te klasör yok → prefix altındaki tüm objeleri sayfalayarak sil.
        var prefix = relativeFolder.Trim('/') + "/";
        string? token = null;
        do
        {
            var list = await _s3.ListObjectsV2Async(new ListObjectsV2Request
            {
                BucketName = _bucket,
                Prefix = prefix,
                ContinuationToken = token,
            }, ct);

            if (list.S3Objects is { Count: > 0 })
            {
                await _s3.DeleteObjectsAsync(new DeleteObjectsRequest
                {
                    BucketName = _bucket,
                    Objects = list.S3Objects.Select(o => new KeyVersion { Key = o.Key }).ToList(),
                }, ct);
            }

            token = (list.IsTruncated ?? false) ? list.NextContinuationToken : null;
        }
        while (token is not null);
    }

    // ── Yardımcılar ──

    private static string Combine(string? subFolder, string name)
        => string.IsNullOrEmpty(subFolder) ? name : $"{subFolder.Trim('/')}/{name}";

    // Dosya adından dizin bileşenlerini + geçersiz karakterleri atar (key prefix injection'ı engellenir).
    private static string SanitizeFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return "dosya";
        var name = fileName.Replace('\\', '/');
        name = name[(name.LastIndexOf('/') + 1)..];   // son '/' sonrası = saf ad
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        name = name.Trim();
        return (string.IsNullOrEmpty(name) || name is "." or "..") ? "dosya" : name;
    }

    // S3 key sınırı 1024 bayt; guid öneki + ad için makul bir tavan.
    private static string Truncate(string name) => name.Length <= 200 ? name : name[..200];

    private static string GuessContentType(string key) => Path.GetExtension(key).ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" => "image/jpeg",
        ".png" => "image/png",
        ".webp" => "image/webp",
        ".gif" => "image/gif",
        ".pdf" => "application/pdf",
        _ => "application/octet-stream",
    };
}
