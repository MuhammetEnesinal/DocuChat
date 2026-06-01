namespace DocuChat.Application.ServiceContracts;

public record AnswerQualityResult(double Score, IReadOnlyList<string> Issues, bool Validated = true)
{
    // Validation gerçekten çalıştı + sorun yok.
    public static AnswerQualityResult Good() => new(1.0, Array.Empty<string>(), true);

    // Validation çalıştı + somut sorun bulundu.
    public static AnswerQualityResult Failed(string reason) => new(0.0, new[] { reason }, true);

    // Validation çağrısı patladı/timeout — sahte güven verme. Cevap sunulur ama cache'lenmez.
    public static AnswerQualityResult Unvalidated() => new(1.0, Array.Empty<string>(), false);
}
