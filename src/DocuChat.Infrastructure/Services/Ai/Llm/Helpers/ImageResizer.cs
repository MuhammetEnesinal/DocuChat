namespace DocuChat.Infrastructure.Services.Ai.Llm.Helpers;

internal static class ImageResizer
{
    // Görsel hedef boyutu aşıyorsa JPEG q=85 ile boyutlandırır; küçükse dokunmaz.
    // Hata olursa orijinal byte'ları döner (fail-open).
    public static byte[] ResizeIfNeeded(byte[] bytes, int maxDim, int skipBelow = 800)
    {
        try
        {
            using var input = SkiaSharp.SKBitmap.Decode(bytes);
            if (input is null) return bytes;

            var maxSide = Math.Max(input.Width, input.Height);

            var threshold = Math.Max(skipBelow, maxDim);
            if (maxSide <= threshold) return bytes;

            var scale = (double)maxDim / maxSide;
            var newW = (int)(input.Width * scale);
            var newH = (int)(input.Height * scale);

            using var resized = input.Resize(new SkiaSharp.SKImageInfo(newW, newH), SkiaSharp.SKSamplingOptions.Default);
            if (resized is null) return bytes;
            using var image = SkiaSharp.SKImage.FromBitmap(resized);
            using var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Jpeg, quality: 85);
            return data.ToArray();
        }
        catch
        {
            return bytes;
        }
    }
}
