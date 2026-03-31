using DocuChat.Domain.Enums;

namespace DocuChat.Application.Abstractions;

public interface IDocumentParser
{
    IEnumerable<string> Parse(Stream stream, FileType fileType);
}