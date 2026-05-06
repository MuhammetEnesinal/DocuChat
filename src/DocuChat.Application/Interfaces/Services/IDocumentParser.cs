using DocuChat.Domain.Enums;

namespace DocuChat.Application.Interfaces.Services;

public interface IDocumentParser
{
    Task<IEnumerable<ParsedChunk>> ParseAsync(Stream stream, FileType fileType);
}