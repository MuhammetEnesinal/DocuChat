using DocuChat.Domain.Enums;
using DocuChat.Application.ServiceContracts;

namespace DocuChat.Application.Interfaces.Services;

public interface IDocumentParser
{
    Task<IEnumerable<ParsedChunk>> ParseAsync(Stream stream, FileType fileType, CancellationToken ct = default);
}
