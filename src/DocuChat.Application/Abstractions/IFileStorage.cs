namespace DocuChat.Application.Abstractions;

public interface IFileStorage
{
    /// Dosyayı fiziksel olarak kaydeder, yolu döner.
    Task<string> SaveAsync(Stream stream, string fileName, CancellationToken ct = default);

    //Fiziksel dosyayı siler.
    Task DeleteAsync(string storagePath, CancellationToken ct = default);

    /// Dosyayı okumak için stream döner.
    Stream Read(string storagePath);
}