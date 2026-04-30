using DocuChat.Application.Common;
using DocuChat.Application.DTOs.Document;

namespace DocuChat.Application.Interfaces.Services;

public interface IDocumentService
{
    Task<Result<DocumentResponseDto>> UploadAsync(UploadDocumentRequest request, CancellationToken ct = default);
    Task<Result<IReadOnlyList<DocumentResponseDto>>> GetAllDocumentsAsync(CancellationToken ct = default);
    Task<Result<bool>> DeleteAsync(Guid id, CancellationToken ct = default);
    Task<Result<IReadOnlyList<DocumentChunkDto>>> GetChunksAsync(Guid id, CancellationToken ct = default);
    Task<Result<DocumentResponseDto>> ReprocessAsync(Guid id, CancellationToken ct = default);
    Task<Result<(string StoragePath, string ContentType, string FileName)>> GetFileInfoAsync(Guid id, CancellationToken ct = default);
}