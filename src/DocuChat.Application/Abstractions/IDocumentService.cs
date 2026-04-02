using DocuChat.Application.Common;
using DocuChat.Application.DTOs.Document;

namespace DocuChat.Application.Abstractions;

public interface IDocumentService
{
    Task<Result<DocumentResponseDto>> UploadAsync(UploadDocumentRequest request, CancellationToken ct = default);
    Task<Result<IReadOnlyList<DocumentResponseDto>>> GetAllDocumentsAsync(CancellationToken ct = default);
    Task<Result<bool>> DeleteAsync(Guid id, CancellationToken ct = default);
}