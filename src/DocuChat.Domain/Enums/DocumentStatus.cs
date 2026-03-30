namespace DocuChat.Domain.Enums;

public enum DocumentStatus
{
    Pending = 0,   // yüklendi, bekliyor
    Processing = 1,   // parse + embed sürüyor
    Ready = 2,   // soru sorulmaya hazır
    Failed = 3    // hata oluştu, ErrorMessage dolu
}