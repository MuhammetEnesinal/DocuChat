namespace DocuChat.Application.Common;

/// <summary>
/// Final cevap post-validation (7B) sonucu.
/// Score < 0.7 → kullanıcıya uyarı banner'ı eklenir, cache'e yazılmaz.
/// </summary>
public record AnswerQualityResult(double Score, IReadOnlyList<string> Issues)
{
    public static AnswerQualityResult Good() => new(1.0, Array.Empty<string>());
    public static AnswerQualityResult Failed(string reason) => new(0.0, new[] { reason });
}
