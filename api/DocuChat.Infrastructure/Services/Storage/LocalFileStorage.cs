// DocuChat.Infrastructure/Services/LocalFileStorage.cs
using System.Text;
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
        Stream stream, string fileName, string? subFolder = null, CancellationToken ct = default)
    {
        // fileName KULLANICIDAN gelir (upload'daki dosya adı) — sanitize edilmeden path'e
        // konursa "../../../tmp/x" gibi bir ad depolama kökünün dışına yazar (path traversal).
        // Benzersizlik öneki dosya adına 37 karakter ekler; ad, dosya sistemi sınırına sığacak
        // şekilde kırpılır.
        var prefix = $"{Guid.NewGuid()}_";
        var uniqueName = prefix + TruncateToByteLimit(SanitizeFileName(fileName), MaxComponentBytes - prefix.Length);
        return await WriteAsync(stream, Combine(subFolder, uniqueName), ct);
    }

    public async Task<string> SaveRawAsync(
        Stream stream, string exactFileName, string? subFolder = null, CancellationToken ct = default)
    {
        var safe = TruncateToByteLimit(SanitizeFileName(exactFileName), MaxComponentBytes);
        return await WriteAsync(stream, Combine(subFolder, safe), ct);
    }

    // Linux dosya sistemlerinde tek bir yol bileşeni en fazla 255 bayt olabilir; sınır karakter
    // değil bayt cinsindendir.
    private const int MaxComponentBytes = 255;

    // Adı UTF-8 bayt sınırına kırpar ve uzantıyı korur. Ölçüm bayt üzerinden yapılır: Türkçe
    // harfler UTF-8'de iki bayt tuttuğundan, karakter sayısı sınırın altında olan bir ad bayt
    // olarak sınırı aşabilir. Kırpma, çok baytlı bir karakterin ortasından bölmeyecek şekilde
    // sınırın altında kalan son tam karakterde durur.
    private static string TruncateToByteLimit(string name, int maxBytes)
    {
        if (maxBytes <= 0) return "dosya";
        if (Encoding.UTF8.GetByteCount(name) <= maxBytes) return name;

        var ext = Path.GetExtension(name);
        // Uzantı tek başına bütçeye sığmıyorsa atılır.
        if (Encoding.UTF8.GetByteCount(ext) > maxBytes) ext = string.Empty;

        var stem = Path.GetFileNameWithoutExtension(name);
        var stemBudget = maxBytes - Encoding.UTF8.GetByteCount(ext);

        // Karakterler tek tek eklenir ve bütçe aşılmadan durulur; bu sayede çok baytlı bir
        // karakter ortadan bölünmez.
        var sb = new StringBuilder();
        var used = 0;
        foreach (var ch in stem)
        {
            var size = Encoding.UTF8.GetByteCount(ch.ToString());
            if (used + size > stemBudget) break;
            sb.Append(ch);
            used += size;
        }

        var result = sb.ToString() + ext;
        return string.IsNullOrWhiteSpace(result) ? "dosya" : result;
    }

    // Dosya adından dizin bileşenlerini ve geçersiz karakterleri atar → yalnız düz bir ad kalır.
    // Hem '/' hem '\' kesilir: container Linux olsa da istemci Windows-stil ad gönderebilir.
    private static string SanitizeFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return "dosya";

        var name = fileName.Replace('\\', '/');
        name = name[(name.LastIndexOf('/') + 1)..];          // son '/' sonrası = saf dosya adı
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        name = name.Trim();

        // "." / ".." gibi özel adlar dosya adı olamaz.
        if (string.IsNullOrEmpty(name) || name == "." || name == "..") return "dosya";
        return name;
    }

    // Alt klasör (varsa) ile dosya adını URL uyumlu '/' ayracıyla birleştirir.
    private static string Combine(string? subFolder, string name)
        => string.IsNullOrEmpty(subFolder) ? name : $"{subFolder.Trim('/')}/{name}";

    // SAVUNMA KATMANI 2: çözülen mutlak yol depolama kökünün ALTINDA olmalı. Sanitize atlansa
    // veya DB'de eski/bozuk bir path bulunsa bile kök dışına okuma/yazma/silme engellenir.
    private string ResolveWithinBase(string relativePath)
    {
        var baseFull = Path.GetFullPath(_basePath);
        var full = Path.GetFullPath(Path.Combine(baseFull, relativePath));

        if (!full.StartsWith(baseFull + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            && !string.Equals(full, baseFull, StringComparison.Ordinal))
        {
            _logger.LogWarning("[FileStorage] Depolama kökü dışına erişim ENGELLENDİ: {Path}", relativePath);
            throw new UnauthorizedAccessException("Depolama kökü dışına erişim engellendi.");
        }
        return full;
    }

    public Task DeleteAsync(string storagePath, CancellationToken ct = default)
    {
        var fullPath = ResolveWithinBase(storagePath);
        if (File.Exists(fullPath))
            File.Delete(fullPath);
        return Task.CompletedTask;
    }

    public Task DeleteDirectoryAsync(string relativeFolder, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(relativeFolder)) return Task.CompletedTask;
        var fullPath = ResolveWithinBase(relativeFolder);
        if (Directory.Exists(fullPath))
            Directory.Delete(fullPath, recursive: true);
        return Task.CompletedTask;
    }

    public Stream OpenRead(string storagePath)
    {
        var fullPath = ResolveWithinBase(storagePath);
        _logger.LogDebug("[FileStorage] OpenRead: {FullPath} | Exists: {Exists}", fullPath, File.Exists(fullPath));
        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"Dosya bulunamadı: {fullPath}");
        return File.OpenRead(fullPath);
    }

    private async Task<string> WriteAsync(
        Stream stream, string relativePath, CancellationToken ct)
    {
        var fullPath = ResolveWithinBase(relativePath);
        // Alt klasörlü yollarda hedef dizin yoksa oluştur.
        var dir = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        await using var fs = File.Create(fullPath);
        // Caller stream'i kısmen okumuş olabilir — seekable ise baştan başla.
        if (stream.CanSeek) stream.Position = 0;
        await stream.CopyToAsync(fs, ct);
        return relativePath;
    }
}
