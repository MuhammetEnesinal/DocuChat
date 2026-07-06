using DocuChat.Domain.Enums;
using DocuChat.Application.ServiceContracts;

namespace DocuChat.Application.Interfaces.Services.Documents;

public interface IDocumentParser
{
    // imageSubFolder verilirse, belgeden çıkarılan görseller o alt klasör altına kaydedilir
    // (belge başına ayrı klasör). Verilmezse görseller düz olarak taban klasöre yazılır.
    Task<IEnumerable<ParsedChunk>> ParseAsync(Stream stream, FileType fileType, string? imageSubFolder = null, CancellationToken ct = default);
}
