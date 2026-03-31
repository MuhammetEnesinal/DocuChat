using Microsoft.Extensions.Configuration;
using DocuChat.Application.Abstractions;

namespace DocuChat.Infrastructure.Services;

public class LocalFileStorage : IFileStorage
{
    private readonly string _basePath;

    public LocalFileStorage(IConfiguration cfg)
    {
        _basePath = cfg["Storage:LocalPath"] ?? "uploads";
        Directory.CreateDirectory(_basePath);
    }

    public async Task<string> SaveAsync(
        Stream stream, string fileName, CancellationToken ct = default)
    {
        var uniqueName = $"{Guid.NewGuid()}_{fileName}";
        var fullPath = Path.Combine(_basePath, uniqueName);

        await using var fs = File.Create(fullPath);
        stream.Position = 0;
        await stream.CopyToAsync(fs, ct);

        return uniqueName;
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
        var fullPath = Path.Combine(_basePath, storagePath);
        return File.OpenRead(fullPath);
    }
}