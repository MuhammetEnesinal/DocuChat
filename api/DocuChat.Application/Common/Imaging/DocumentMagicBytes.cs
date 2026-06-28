using DocuChat.Domain.Enums;

namespace DocuChat.Application.Common.Imaging;

/// <summary>
/// Belge dosyalarını uzantı / browser MIME yerine içerikten (magic byte) doğrular.
/// Kullanıcı .pdf uzantılı bir ZIP yükleyemez → parser fail + disk israfı önlenir.
/// CSV için içerik signature yok (text format) — declared type'a güvenilir.
/// </summary>
public static class DocumentMagicBytes
{
    /// <summary>
    /// Header byte'larından belge formatı tespit eder. Tanınmayan format için Unknown.
    /// </summary>
    public static DocumentFormat DetectFormat(ReadOnlySpan<byte> b)
    {
        // PDF: %PDF- (25 50 44 46 2D)
        if (b.Length >= 5 && b[0] == 0x25 && b[1] == 0x50 && b[2] == 0x44 && b[3] == 0x46 && b[4] == 0x2D)
            return DocumentFormat.Pdf;
        // DOCX/XLSX: ZIP/OOXML — "PK\003\004" (50 4B 03 04)
        if (b.Length >= 4 && b[0] == 0x50 && b[1] == 0x4B && b[2] == 0x03 && b[3] == 0x04)
            return DocumentFormat.Ooxml;
        // DOC: OLE2 Compound — D0 CF 11 E0 A1 B1 1A E1
        if (b.Length >= 8 && b[0] == 0xD0 && b[1] == 0xCF && b[2] == 0x11 && b[3] == 0xE0
            && b[4] == 0xA1 && b[5] == 0xB1 && b[6] == 0x1A && b[7] == 0xE1)
            return DocumentFormat.OleDoc;
        return DocumentFormat.Unknown;
    }

    /// <summary>
    /// Declared FileType ile içerik signature'ı tutarlı mı? CSV'de signature yok → daima true.
    /// </summary>
    public static bool MatchesDeclaredType(ReadOnlySpan<byte> b, FileType declared)
    {
        var detected = DetectFormat(b);
        return declared switch
        {
            FileType.Pdf  => detected == DocumentFormat.Pdf,
            FileType.Docx => detected == DocumentFormat.Ooxml,
            FileType.Xlsx => detected == DocumentFormat.Ooxml,
            FileType.Doc  => detected == DocumentFormat.OleDoc,
            FileType.Csv  => true,    // text format, magic byte yok
            _             => false
        };
    }
}

public enum DocumentFormat
{
    Unknown,
    Pdf,
    Ooxml,
    OleDoc
}
