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

    public async Task<Result<(string StoragePath, string ContentType, string FileName)>> GetFileInfoAsync(Guid id, CancellationToken ct)
    {
        var doc = await _uow.Documents.GetByIdAsync(id, ct);
        if (doc is null || doc.StoragePath is null)
            return Result<(string, string, string)>.Failure(Error.NotFound("Belge bulunamadı."));

        var contentType = doc.ContentType == "application/pdf" ? "application/pdf" : "application/octet-stream";
        return Result<(string, string, string)>.Success((doc.StoragePath, contentType, doc.FileName));
    }


    public async Task<Result<IReadOnlyList<DocumentChunkDto>>> GetChunksAsync(Guid id, CancellationToken ct)
    {
        var doc = await _uow.Documents.GetByIdAsync(id, ct);
        if (doc is null)
            return Result<IReadOnlyList<DocumentChunkDto>>.Failure(Error.NotFound("Belge bulunamadı."));

        var chunks = await _uow.Chunks.FindAsync(c => c.DocumentId == id, ct);
        var dtos = chunks
            .OrderBy(c => c.ChunkIndex)
            .Select(c => c.Adapt<DocumentChunkDto>())
            .ToList();

        return Result<IReadOnlyList<DocumentChunkDto>>.Success(dtos);
    }

    public async Task<Result<DocumentResponseDto>> ReprocessAsync(Guid id, CancellationToken ct)
    {
        var doc = await _uow.Documents.GetByIdAsync(id, ct);
        if (doc is null)
            return Result<DocumentResponseDto>.Failure(Error.NotFound("Belge bulunamadı."));

        if (doc.StoragePath is null)
            return Result<DocumentResponseDto>.Failure(Error.Validation("Orijinal dosya bulunamadı."));

        // 1. Mevcut chunk'ları sil
        var existingChunks = await _uow.Chunks.FindAsync(c => c.DocumentId == id, ct);
        foreach (var chunk in existingChunks)
            _uow.Chunks.Delete(chunk);

        // 2. Belgeyi Processing durumuna al
        doc.Status = DocumentStatus.Processing;
        doc.ErrorMessage = null;
        doc.ChunkCount = 0;
        doc.UpdatedAt = DateTime.UtcNow;
        await _uow.SaveChangesAsync(ct);

        _logger.LogInformation("[Reprocess] Başlatıldı: {DocId} - {FileName}", id, doc.FileName);

        try
        {
            // 3. Dosyayı storage'dan oku
            using var stream = _fileStorage.Read(doc.StoragePath);

            // 4. Parse et
            var chunks = _parser.Parse(stream, doc.FileType);
            int idx = 0;

            // 5. Embed et ve kaydet
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
            _logger.LogInformation("[Reprocess] Tamamlandı: {DocId} - {Count} chunk", id, idx);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Reprocess] Başarısız: {DocId}", id);
            doc.Status = DocumentStatus.Failed;
            doc.ErrorMessage = ex.Message;
            doc.UpdatedAt = DateTime.UtcNow;
        }

        await _uow.SaveChangesAsync(ct);
        return Result<DocumentResponseDto>.Success(doc.Adapt<DocumentResponseDto>());
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