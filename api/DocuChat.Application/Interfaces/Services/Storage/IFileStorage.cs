
namespace DocuChat.Application.Interfaces.Services.Storage;

public interface IFileStorage
{
    // Dosyayı benzersiz ({guid}_ad) adla kaydeder. subFolder verilirse o alt klasör altına
    // yazılır (klasör yoksa oluşturulur). Döner: taban klasöre göreli, '/' ayraçlı yol.
    Task<string> SaveAsync(Stream stream, string fileName, string? subFolder = null, CancellationToken ct = default);

    // Dosyayı verilen adın aynısıyla kaydeder. subFolder verilirse o alt klasör altına yazılır.
    Task<string> SaveRawAsync(Stream stream, string exactFileName, string? subFolder = null, CancellationToken ct = default);

    Task DeleteAsync(string storagePath, CancellationToken ct = default);

    // Belgeye ait alt klasörü (varsa) içeriğiyle birlikte kaldırır. Düz yapıdaki (alt klasörsüz)
    // eski belgelerde böyle bir klasör olmadığından no-op'tur.
    Task DeleteDirectoryAsync(string relativeFolder, CancellationToken ct = default);

    // Disk üzerindeki dosyayı okumak için lazy stream açar (caller dispose etmeli).
    // FileNotFoundException fırlatabilir.
    Stream OpenRead(string storagePath);
}
