using DocuChat.Application.Common;
using DocuChat.Application.DTOs.Document;

namespace DocuChat.Application.Interfaces.UseCases;

public interface IDocumentUseCase
{
    Task<Result<DocumentResponseDto>> UploadAsync(UploadDocumentRequest request, CancellationToken ct = default);
    Task<Result<IReadOnlyList<DocumentResponseDto>>> GetAllDocumentsAsync(string? search = null, CancellationToken ct = default);
    Task<Result<PaginatedResult<DocumentResponseDto>>> GetAllDocumentsPagedAsync(int page, int pageSize, CancellationToken ct = default);
    Task<Result<bool>> DeleteAsync(Guid id, CancellationToken ct = default);
    Task<Result<int>> DeleteBatchAsync(IEnumerable<Guid> ids, CancellationToken ct = default);
    Task<Result<IReadOnlyList<DocumentChunkResponseDto>>> GetChunksAsync(Guid id, CancellationToken ct = default);
    Task<Result<DocumentResponseDto>> ReprocessAsync(Guid id, CancellationToken ct = default);

    /// Belgeyi stream olarak döner — controller IFileStorage'a dokunmaz.
    Task<Result<(Stream FileStream, string ContentType, string FileName)>> GetFileStreamAsync(
        Guid id, CancellationToken ct = default);
}

