using Mapster;
using Microsoft.Extensions.Logging;
using DocuChat.Application.Interfaces.Services;
using DocuChat.Application.Interfaces.Repositories;
using DocuChat.Application.Common;
using DocuChat.Application.DTOs.Document;
using DocuChat.Domain.Entities;
using DocuChat.Domain.Enums;

namespace DocuChat.Application.Services;

public class DocumentService : IDocumentService
{
    private readonly IUnitOfWork _uow;
    private readonly IDocumentParser _parser;
    private readonly IEmbeddingService _embedder;
    private readonly IFileStorage _fileStorage;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<DocumentService> _logger;

    public DocumentService(
        IUnitOfWork uow, IDocumentParser parser, IEmbeddingService embedder,
        IFileStorage fileStorage, ICurrentUser currentUser,
        ILogger<DocumentService> logger)
    {
        _uow = uow;
        _parser = parser;
        _embedder = embedder;
        _fileStorage = fileStorage;
        _currentUser = currentUser;
        _logger = logger;
    }

    public async Task<Result<DocumentResponseDto>> UploadAsync(
        UploadDocumentRequest req, CancellationToken ct)
    {
        req.FileStream.Position = 0;
        var storagePath = await _fileStorage.SaveAsync(req.FileStream, req.FileName, ct);

        var doc = new Document
        {
            UserId = _currentUser.UserId,
            FileName = req.FileName,
            ContentType = req.ContentType,
            FileSizeBytes = req.FileSizeBytes,
            StoragePath = storagePath,
            FileType = DetectFileType(req.ContentType),
            Status = DocumentStatus.Pending,
        };

        await _uow.Documents.AddAsync(doc, ct);
        await _uow.SaveChangesAsync(ct);

        try
        {
            doc.Status = DocumentStatus.Processing;
            doc.UpdatedAt = DateTime.UtcNow;
            await _uow.SaveChangesAsync(ct);

            req.FileStream.Position = 0;
            var chunks = _parser.Parse(req.FileStream, doc.FileType);
            int idx = 0;

            foreach (var parsed in chunks)
            {
                var vec = await _embedder.GetEmbeddingAsync(parsed.Content, ct);
                await _uow.Chunks.AddAsync(new DocumentChunk
                {
                    DocumentId = doc.Id,
                    Content = parsed.Content,
                    ChunkIndex = idx++,
                    Embedding = vec,
                    ImagePath = parsed.ImagePath,
                }, ct);
            }

            doc.Status = DocumentStatus.Ready;
            doc.ChunkCount = idx;
            doc.UpdatedAt = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Parse/embed başarısız. DocId: {DocId}", doc.Id);
            doc.Status = DocumentStatus.Failed;
            doc.ErrorMessage = ex.Message;
            doc.UpdatedAt = DateTime.UtcNow;
        }

        await _uow.SaveChangesAsync(ct);
        return Result<DocumentResponseDto>.Success(doc.Adapt<DocumentResponseDto>());
    }

    public async Task<Result<IReadOnlyList<DocumentResponseDto>>> GetAllDocumentsAsync(
        CancellationToken ct)
    {
        var docs = await _uow.Documents.GetAllAsync(ct);
        var dtos = docs.Select(d => d.Adapt<DocumentResponseDto>()).ToList();
        return Result<IReadOnlyList<DocumentResponseDto>>.Success(dtos);
    }

    public async Task<Result<bool>> DeleteAsync(Guid docId, CancellationToken ct)
    {
        var doc = await _uow.Documents.GetByIdAsync(docId, ct);
        if (doc is null)
            return Result<bool>.Failure(Error.NotFound("Belge bulunamadı."));

        if (doc.StoragePath is not null)
            await _fileStorage.DeleteAsync(doc.StoragePath, ct);

        _uow.Documents.Delete(doc);
        await _uow.SaveChangesAsync(ct);
        return Result<bool>.Success(true);
    }

    private static FileType DetectFileType(string contentType) => contentType switch
    {
        "application/pdf" => FileType.Pdf,
        "application/msword" => FileType.Doc,
        var t when t.Contains("wordprocessingml") => FileType.Docx,
        var t when t.Contains("spreadsheetml") => FileType.Xlsx,
        "text/csv" => FileType.Csv,
        _ => FileType.Txt,
    };
}