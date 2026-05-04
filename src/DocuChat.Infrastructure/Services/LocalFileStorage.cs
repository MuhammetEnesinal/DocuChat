// DocuChat.Infrastructure/Services/LocalFileStorage.cs
using Microsoft.Extensions.Configuration;
using DocuChat.Application.Interfaces.Services;

namespace DocuChat.Infrastructure.Services;

public class LocalFileStorage : IFileStorage
{
    private readonly string _basePath;

    public LocalFileStorage(IConfiguration cfg)
    {
        _basePath = cfg["Storage:LocalPath"] ?? "uploads";
        Directory.CreateDirectory(_basePath);
    }

    /// <summary>
    /// Belge yüklemede kullanılır. Guid prefix eklenerek isim çakışması önlenir.
    /// </summary>
    public async Task<string> SaveAsync(
        Stream stream, string fileName, CancellationToken ct = default)
    {
        var uniqueName = $"{Guid.NewGuid()}_{fileName}";
        return await WriteAsync(stream, uniqueName, ct);
    }

    /// <summary>
    /// Parser'ın ürettiği resimler için — isim olduğu gibi kaydedilir, çift Guid olmaz.
    /// </summary>
    public async Task<string> SaveRawAsync(
        Stream stream, string exactFileName, CancellationToken ct = default)
    {
        return await WriteAsync(stream, exactFileName, ct);
    }

    public Task DeleteAsync(string storagePath, CancellationToken ct = default)
    {
        var fullPath = Path.Combine(_basePath, storagePath);
        if (File.Exists(fullPath))
            File.Delete(fullPath);
        return Task.CompletedTask;
    }

    public Stream Read(string storagePath)
    {
        var fullPath = Path.GetFullPath(Path.Combine(_basePath, storagePath));
        Console.WriteLine($"[FileStorage] Read: {fullPath} | Exists: {File.Exists(fullPath)}");
        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"Dosya bulunamadı: {fullPath}");
        return File.OpenRead(fullPath);
    }

    // ── ortak yazma mantığı ───────────────────────────────────────────────
    private async Task<string> WriteAsync(
        Stream stream, string fileName, CancellationToken ct)
    {
        var fullPath = Path.Combine(_basePath, fileName);
        await using var fs = File.Create(fullPath);
        stream.Position = 0;
        await stream.CopyToAsync(fs, ct);
        return fileName;
    }
}