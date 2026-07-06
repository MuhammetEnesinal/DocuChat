
namespace DocuChat.Application.Interfaces.Services.Storage;

public interface IFileStorage
{
   
    Task<string> SaveAsync(Stream stream, string fileName, CancellationToken ct = default);

    
    Task<string> SaveRawAsync(Stream stream, string exactFileName, CancellationToken ct = default);

    Task DeleteAsync(string storagePath, CancellationToken ct = default);

    // Disk üzerindeki dosyayı okumak için lazy stream açar (caller dispose etmeli).
    // FileNotFoundException fırlatabilir.
    Stream OpenRead(string storagePath);
}
