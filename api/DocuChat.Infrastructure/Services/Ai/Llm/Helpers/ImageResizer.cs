namespace DocuChat.Infrastructure.Services.Ai.Llm.Helpers;

// Resize sonucu: bytes + gerçek MIME (resize edildi ise her zaman image/jpeg).
internal class ResizedImage
{
    public byte[] Bytes { get; set; }
    public string MimeType { get; set; }

    public ResizedImage(byte[] Bytes, string MimeType)
    {
        this.Bytes = Bytes;
        this.MimeType = MimeType;
    }
}

internal static class ImageResizer
{
    // Görsel hedef boyutu aşıyorsa JPEG q=85 ile boyutlandırır; küçükse dokunmaz.
    // Resize yapılırsa MimeType = "image/jpeg" (encoder output). Aksi halde caller'ın bildirdiği mime.
    // Hata olursa orijinal byte'lar + orijinal mime döner (fail-open).
    public static ResizedImage ResizeIfNeeded(byte[] bytes, string originalMime, int maxDim, int skipBelow = 800)
    {
        try
        {
            using var input = SkiaSharp.SKBitmap.Decode(bytes);
            if (input is null) return new ResizedImage(bytes, originalMime);

            var maxSide = Math.Max(input.Width, input.Height);

            var threshold = Math.Max(skipBelow, maxDim);
            if (maxSide <= threshold) return new ResizedImage(bytes, originalMime);

            var scale = (double)maxDim / maxSide;
            var newW = (int)(input.Width * scale);
            var newH = (int)(input.Height * scale);

            using var resized = input.Resize(new SkiaSharp.SKImageInfo(newW, newH), SkiaSharp.SKSamplingOptions.Default);
            if (resized is null) return new ResizedImage(bytes, originalMime);
            using var image = SkiaSharp.SKImage.FromBitmap(resized);
            using var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Jpeg, quality: 85);
            return new ResizedImage(data.ToArray(), "image/jpeg");
        }
        catch
        {
            return new ResizedImage(bytes, originalMime);
        }
    }
}
