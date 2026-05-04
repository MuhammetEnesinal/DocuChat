
namespace DocuChat.Application.Interfaces.Services;

public interface IFileStorage
{
   
    Task<string> SaveAsync(Stream stream, string fileName, CancellationToken ct = default);

    
    Task<string> SaveRawAsync(Stream stream, string exactFileName, CancellationToken ct = default);

    Task DeleteAsync(string storagePath, CancellationToken ct = default);

    Stream Read(string storagePath);
}