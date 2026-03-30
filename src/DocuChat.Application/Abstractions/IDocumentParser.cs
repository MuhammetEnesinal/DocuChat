using DocuChat.Domain.Enums;

namespace DocuChat.Application.Abstractions;

public interface IDocumentParser
{
   
    /// Dosyayı parse eder, chunk'lanmış metin parçalarını döner.
    /// Her string ~800 token, ardışık chunk'lar 100 token örtüşür.
   
    IEnumerable<string> Parse(Stream stream, FileType fileType);
}