namespace DocuChat.Application.ServiceContracts;

// LLM cevabında korunacak görsel işaretinin ([[IMG-K]]) çözüm bilgisi.
// K (global sıra no) → bu kayıt. Path: /uploads altındaki dosya; Caption: Pixtral açıklaması (alt text).
public class AnswerImageRef
{
    public string Path { get; set; }
    public string? Caption { get; set; }

    public AnswerImageRef(string Path, string? Caption)
    {
        this.Path = Path;
        this.Caption = Caption;
    }
}

// Cevap üretimi için hazırlanmış LLM context'i. ChatUseCase önce bunu kurar (BuildAnswerContext),
// sonra StreamAnswerAsync ile stream eder, en sonunda cevaptaki [[IMG-K]] işaretlerini
// ImageMap üzerinden gerçek görsel markdown'ına çevirir.
// Tasarım: LLM görseli SEÇMEZ/YERLEŞTİRMEZ — sadece içerikteki kısa [[IMG-K]] işaretini
// olduğu yerde korur. Görseli KOD yerleştirir (deterministik). Bu sayede LLM'in markdown'ı
// bozması / atlaması / yanlış görsel koyması sorunu ortadan kalkar.
public class AnswerContext
{
    public string Context { get; set; }
    public IReadOnlyList<string> VisionImageUrls { get; set; }
    public IReadOnlyDictionary<int, AnswerImageRef> ImageMap { get; set; }

    public AnswerContext(
        string Context,
        IReadOnlyList<string> VisionImageUrls,
        IReadOnlyDictionary<int, AnswerImageRef> ImageMap)
    {
        this.Context = Context;
        this.VisionImageUrls = VisionImageUrls;
        this.ImageMap = ImageMap;
    }

    public static AnswerContext Empty { get; } =
        new(string.Empty, Array.Empty<string>(), new Dictionary<int, AnswerImageRef>());
}
