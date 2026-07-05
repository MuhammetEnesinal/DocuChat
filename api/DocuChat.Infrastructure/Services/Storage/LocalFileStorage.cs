// DocuChat.Infrastructure/Services/LocalFileStorage.cs
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
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

namespace DocuChat.Infrastructure.Services.Storage;

public class LocalFileStorage : IFileStorage
{
    private readonly string _basePath;
    private readonly ILogger<LocalFileStorage> _logger;

    public LocalFileStorage(IConfiguration cfg, ILogger<LocalFileStorage> logger)
    {
        _basePath = cfg["Storage:LocalPath"] ?? "uploads";
        _logger = logger;
        Directory.CreateDirectory(_basePath);
    }

    public async Task<string> SaveAsync(
        Stream stream, string fileName, CancellationToken ct = default)
    {
        var uniqueName = $"{Guid.NewGuid()}_{fileName}";
        return await WriteAsync(stream, uniqueName, ct);
    }

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

    public Stream OpenRead(string storagePath)
    {
        var fullPath = Path.GetFullPath(Path.Combine(_basePath, storagePath));
        _logger.LogDebug("[FileStorage] OpenRead: {FullPath} | Exists: {Exists}", fullPath, File.Exists(fullPath));
        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"Dosya bulunamadı: {fullPath}");
        return File.OpenRead(fullPath);
    }

    private async Task<string> WriteAsync(
        Stream stream, string fileName, CancellationToken ct)
    {
        var fullPath = Path.Combine(_basePath, fileName);
        await using var fs = File.Create(fullPath);
        // Caller stream'i kısmen okumuş olabilir — seekable ise baştan başla.
        if (stream.CanSeek) stream.Position = 0;
        await stream.CopyToAsync(fs, ct);
        return fileName;
    }
}
