using DocuChat.Application.Common;
using DocuChat.Application.DTOs.Document;

namespace DocuChat.Application.Abstractions;

public interface IDocumentService
{
    Task<Result<DocumentResponseDto>> UploadAsync(UploadDocumentRequest request, CancellationToken ct = default);
    Task<Result<IReadOnlyList<DocumentResponseDto>>> GetMyDocumentsAsync(CancellationToken ct = default);
    Task<Result<IReadOnlyList<DocumentResponseDto>>> GetAllDocumentsAsync(CancellationToken ct = default); // Admin
    Task<Result<DocumentResponseDto>> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Result<bool>> DeleteAsync(Guid id, CancellationToken ct = default);
}