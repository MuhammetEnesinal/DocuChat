using DocuChat.Domain.Enums;

namespace DocuChat.Application.Common.Imaging;

// Belge dosyalarını uzantı / browser MIME yerine içerikten (magic byte) doğrular.
// Kullanıcı .pdf uzantılı bir ZIP yükleyemez → parser fail + disk israfı önlenir.
// CSV için içerik signature yok (text format) — declared type'a güvenilir.
public static class DocumentMagicBytes
{
    // Header byte'larından belge formatı tespit eder. Tanınmayan format için Unknown.
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
        // RTF: "{\rtf" (7B 5C 72 74 66) — Word belgeleri sıkça .doc uzantısıyla RTF olarak kaydedilir
        if (b.Length >= 5 && b[0] == 0x7B && b[1] == 0x5C && b[2] == 0x72 && b[3] == 0x74 && b[4] == 0x66)
            return DocumentFormat.Rtf;
        // MHTML / HTML: Word "Web Sayfası (tek dosya)" çıktıları da .doc uzantısı taşıyabilir.
        // Metin tabanlı format → magic byte yok, baş kısımdan ASCII sniff.
        if (LooksLikeMarkupText(b))
            return DocumentFormat.MarkupText;
        return DocumentFormat.Unknown;
    }

    // MHTML başlıkları (MIME-Version:, From:, Message-ID:, multipart/related) veya HTML açılışı.
    // BOM/whitespace toleranslı; sadece ilk ~256 bayta bakar.
    private static bool LooksLikeMarkupText(ReadOnlySpan<byte> b)
    {
        var len = Math.Min(b.Length, 256);
        if (len < 5) return false;
        Span<char> chars = stackalloc char[len];
        for (var i = 0; i < len; i++) chars[i] = (char)b[i];   // ASCII varsayımı sniff için yeterli
        var head = new string(chars).TrimStart('﻿', 'ï', '»', '¿', ' ', '\t', '\r', '\n');
        return head.StartsWith("MIME-Version:", StringComparison.OrdinalIgnoreCase)
            || head.StartsWith("Message-ID:", StringComparison.OrdinalIgnoreCase)
            || head.StartsWith("From:", StringComparison.OrdinalIgnoreCase)
            || head.Contains("multipart/related", StringComparison.OrdinalIgnoreCase)
            || head.StartsWith("<!DOCTYPE", StringComparison.OrdinalIgnoreCase)
            || head.StartsWith("<html", StringComparison.OrdinalIgnoreCase);
    }

    // Declared FileType ile içerik signature'ı tutarlı mı? CSV'de signature yok → daima true.
    // Doc: OLE2'nin yanında RTF ve MHTML/HTML de kabul edilir — Word bu formatları da .doc
    // uzantısıyla üretir; parser hepsini işleyebilir (LibreOffice rtf/html → PDF).
    public static bool MatchesDeclaredType(ReadOnlySpan<byte> b, FileType declared)
    {
        var detected = DetectFormat(b);
        return declared switch
        {
            FileType.Pdf  => detected == DocumentFormat.Pdf,
            FileType.Docx => detected == DocumentFormat.Ooxml,
            FileType.Xlsx => detected == DocumentFormat.Ooxml,
            FileType.Doc  => detected is DocumentFormat.OleDoc or DocumentFormat.Rtf or DocumentFormat.MarkupText,
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
    OleDoc,
    Rtf,
    MarkupText
}
