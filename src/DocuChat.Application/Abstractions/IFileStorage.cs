namespace DocuChat.Application.Abstractions;

public interface IFileStorage
{
    Task<string> SaveAsync(Stream stream, string fileName, CancellationToken ct = default);
    Task DeleteAsync(string storagePath, CancellationToken ct = default);
    Stream Read(string storagePath);
}