namespace DocuChat.Application.Common.Imaging;

/// <summary>
/// Magic byte tabanlı resim format tespiti. Browser MIME bilgisi veya dosya uzantısı yerine
/// gerçek içerikten tip belirlenir. JPG/PNG dışı (WebP, GIF, BMP, TIFF) modern formatlar
/// "image/png" fallback'ine düşürülmez → Pixtral caption hata vermez, frontend doğru render.
/// </summary>
public static class ImageMagicBytes
{
    /// <summary>
    /// Resim byte'larından dosya uzantısını döner ("jpg", "png", "gif", "webp", "bmp", "tiff").
    /// Tanımlanamayan format için "png" fallback (en yaygın).
    /// </summary>
    public static string DetectExtension(ReadOnlySpan<byte> bytes) => DetectFormat(bytes) switch
    {
        ImageFormat.Jpeg => "jpg",
        ImageFormat.Png  => "png",
        ImageFormat.Gif  => "gif",
        ImageFormat.WebP => "webp",
        ImageFormat.Bmp  => "bmp",
        ImageFormat.Tiff => "tiff",
        _                => "png"
    };

    /// <summary>
    /// Resim byte'larından MIME tipini döner. Pixtral / Mistral vision API payload'ları için.
    /// Tanımlanamayan format için "image/png" fallback (en yaygın).
    /// </summary>
    public static string DetectMimeType(ReadOnlySpan<byte> bytes) => DetectFormat(bytes) switch
    {
        ImageFormat.Jpeg => "image/jpeg",
        ImageFormat.Png  => "image/png",
        ImageFormat.Gif  => "image/gif",
        ImageFormat.WebP => "image/webp",
        ImageFormat.Bmp  => "image/bmp",
        ImageFormat.Tiff => "image/tiff",
        _                => "image/png"
    };

    public static ImageFormat DetectFormat(ReadOnlySpan<byte> b)
    {
        // JPEG: FF D8 FF
        if (b.Length >= 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF)
            return ImageFormat.Jpeg;
        // PNG: 89 50 4E 47 0D 0A 1A 0A
        if (b.Length >= 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47
            && b[4] == 0x0D && b[5] == 0x0A && b[6] == 0x1A && b[7] == 0x0A)
            return ImageFormat.Png;
        // GIF: 47 49 46 38 (GIF8 — 87a ve 89a versiyonları için ortak)
        if (b.Length >= 4 && b[0] == 0x47 && b[1] == 0x49 && b[2] == 0x46 && b[3] == 0x38)
            return ImageFormat.Gif;
        // WebP: 52 49 46 46 ?? ?? ?? ?? 57 45 42 50  (RIFF....WEBP)
        if (b.Length >= 12 && b[0] == 0x52 && b[1] == 0x49 && b[2] == 0x46 && b[3] == 0x46
            && b[8] == 0x57 && b[9] == 0x45 && b[10] == 0x42 && b[11] == 0x50)
            return ImageFormat.WebP;
        // BMP: 42 4D (BM)
        if (b.Length >= 2 && b[0] == 0x42 && b[1] == 0x4D)
            return ImageFormat.Bmp;
        // TIFF: 49 49 2A 00 (little-endian II*) veya 4D 4D 00 2A (big-endian MM*)
        if (b.Length >= 4 &&
            ((b[0] == 0x49 && b[1] == 0x49 && b[2] == 0x2A && b[3] == 0x00) ||
             (b[0] == 0x4D && b[1] == 0x4D && b[2] == 0x00 && b[3] == 0x2A)))
            return ImageFormat.Tiff;
        return ImageFormat.Unknown;
    }
}

public enum ImageFormat
{
    Unknown,
    Jpeg,
    Png,
    Gif,
    WebP,
    Bmp,
    Tiff
}
