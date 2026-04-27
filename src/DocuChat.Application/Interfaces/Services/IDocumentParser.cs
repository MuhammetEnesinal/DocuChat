using DocuChat.Domain.Enums;

namespace DocuChat.Application.Interfaces.Services;

public interface IDocumentParser
{
    IEnumerable<ParsedChunk> Parse(Stream stream, FileType fileType);
}