
using System.Text.Json;
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
    private readonly IQuestionCacheRepository _cache;      
    private readonly ILogger<DocumentService> _logger;

    public DocumentService(
        IUnitOfWork uow,
        IDocumentParser parser,
        IEmbeddingService embedder,
        IFileStorage fileStorage,
        ICurrentUser currentUser,
        IQuestionCacheRepository cache,
        ILogger<DocumentService> logger)
    {
        _uow = uow;
        _parser = parser;
        _embedder = embedder;
        _fileStorage = fileStorage;
        _currentUser = currentUser;
        _cache = cache;
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
            var chunks = await _parser.ParseAsync(req.FileStream, doc.FileType);
            var addedChunks = new List<DocumentChunk>();
            int idx = 0;

            foreach (var parsed in chunks)
            {
                var vec = await _embedder.GetEmbeddingAsync(parsed.Content, ct);
                var chunk = new DocumentChunk
                {
                    DocumentId = doc.Id,
                    Content = parsed.Content,
                    ChunkIndex = idx++,
                    Embedding = vec,
                    ImagePath = parsed.ImagePath,
                    Header = parsed.Header,
                };
                await _uow.Chunks.AddAsync(chunk, ct);
                addedChunks.Add(chunk);
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
            // Yarım kalan chunk'ları temizle
            var dirtyChunks = await _uow.Chunks.FindAsync(c => c.DocumentId == doc.Id, ct);
            foreach (var c in dirtyChunks) _uow.Chunks.Delete(c);
        }

        await _uow.SaveChangesAsync(ct);
        return Result<DocumentResponseDto>.Success(doc.Adapt<DocumentResponseDto>());
    }

    public async Task<Result<IReadOnlyList<DocumentResponseDto>>> GetAllDocumentsAsync(
        string? search = null, CancellationToken ct = default)
    {
        var docs = await _uow.Documents.GetAllAsync(ct);
        IEnumerable<Document> filtered = docs;
        if (!string.IsNullOrWhiteSpace(search))
            filtered = docs.Where(d => d.FileName.Contains(search, StringComparison.OrdinalIgnoreCase));
        var dtos = filtered.Select(d => d.Adapt<DocumentResponseDto>()).ToList();
        return Result<IReadOnlyList<DocumentResponseDto>>.Success(dtos);
    }

    public async Task<Result<PaginatedResult<DocumentResponseDto>>> GetAllDocumentsPagedAsync(
        int page, int pageSize, CancellationToken ct)
    {
        var allDocs = await _uow.Documents.GetAllAsync(ct);
        var total = allDocs.Count;
        var items = allDocs
            .OrderByDescending(d => d.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(d => d.Adapt<DocumentResponseDto>())
            .ToList();
        return Result<PaginatedResult<DocumentResponseDto>>.Success(
            new PaginatedResult<DocumentResponseDto>(items, total, page, pageSize));
    }

    public async Task<Result<int>> DeleteBatchAsync(IEnumerable<Guid> ids, CancellationToken ct)
    {
        var idList = ids.ToList();
        var deletedIds = new List<Guid>();

        var skipped = new List<Guid>();
        foreach (var id in idList)
        {
            var doc = await _uow.Documents.GetByIdAsync(id, ct);
            if (doc is null) continue;
            if (doc.Status == DocumentStatus.Processing)
            {
                skipped.Add(id);
                _logger.LogWarning("[Delete] Atlandı — belge işleniyor. DocId: {DocId}", id);
                continue;
            }
            await DeleteChunkImagesAsync(id, ct);
            if (doc.StoragePath is not null)
                await _fileStorage.DeleteAsync(doc.StoragePath, ct);
            _uow.Documents.Delete(doc);
            deletedIds.Add(id);
        }

        if (deletedIds.Count > 0)
        {
            await _uow.SaveChangesAsync(ct);
            await _cache.ClearByDocumentIdsAsync(deletedIds, ct);
            _logger.LogInformation("[Batch] {Count} belge silindi, ilgili cache temizlendi", deletedIds.Count);
        }
        return Result<int>.Success(deletedIds.Count);
    }

    public async Task<Result<bool>> DeleteAsync(Guid docId, CancellationToken ct)
    {
        var doc = await _uow.Documents.GetByIdAsync(docId, ct);
        if (doc is null)
            return Result<bool>.Failure(Error.NotFound("Belge bulunamadı."));

        if (doc.Status == DocumentStatus.Processing)
            return Result<bool>.Failure(Error.Validation("Belge işleniyor, lütfen tamamlanmasını bekleyin."));

        await DeleteChunkImagesAsync(docId, ct);
        if (doc.StoragePath is not null)
            await _fileStorage.DeleteAsync(doc.StoragePath, ct);

        _uow.Documents.Delete(doc);
        await _uow.SaveChangesAsync(ct);

        await _cache.ClearByDocumentIdAsync(docId, ct);
        _logger.LogInformation("[Cache] Belge silindi, ilgili cache temizlendi. DocId: {DocId}", docId);

        return Result<bool>.Success(true);
    }

    public async Task<Result<IReadOnlyList<DocumentChunkDto>>> GetChunksAsync(
        Guid id, CancellationToken ct)
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

        doc.Status = DocumentStatus.Processing;
        doc.ErrorMessage = null;
        doc.UpdatedAt = DateTime.UtcNow;
        await _uow.SaveChangesAsync(ct);

        _logger.LogInformation("[Reprocess] Başlatıldı: {DocId} - {FileName}", id, doc.FileName);

        try
        {
            // Önce yeni chunk'ları üret
            using var stream = _fileStorage.Read(doc.StoragePath);
            var parsedChunks = await _parser.ParseAsync(stream, doc.FileType);
            var newChunks = new List<DocumentChunk>();
            int idx = 0;
            foreach (var parsed in parsedChunks)
            {
                var vec = await _embedder.GetEmbeddingAsync(parsed.Content, ct);
                newChunks.Add(new DocumentChunk
                {
                    DocumentId = doc.Id,
                    Content = parsed.Content,
                    ChunkIndex = idx++,
                    Embedding = vec,
                    ImagePath = parsed.ImagePath,
                    Header = parsed.Header,
                });
            }

            // Başarılı olduktan sonra eskileri sil, yenileri ekle
            var existingChunks = await _uow.Chunks.FindAsync(c => c.DocumentId == id, ct);
            foreach (var chunk in existingChunks)
                _uow.Chunks.Delete(chunk);
            foreach (var chunk in newChunks)
                await _uow.Chunks.AddAsync(chunk, ct);

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

        await _cache.ClearByDocumentIdAsync(id, ct);
        _logger.LogInformation("[Cache] Reprocess tamamlandı, ilgili cache temizlendi. DocId: {DocId}", id);

        return Result<DocumentResponseDto>.Success(doc.Adapt<DocumentResponseDto>());
    }

   
    /// Controller IFileStorage'a dokunmadan dosyayı stream olarak alır.

    public async Task<Result<(Stream FileStream, string ContentType, string FileName)>> GetFileStreamAsync(
        Guid id, CancellationToken ct)
    {
        var doc = await _uow.Documents.GetByIdAsync(id, ct);
        if (doc is null || doc.StoragePath is null)
            return Result<(Stream, string, string)>.Failure(Error.NotFound("Belge bulunamadı."));

        if (doc.UserId != _currentUser.UserId && !_currentUser.IsInRole(Roles.Admin))
            return Result<(Stream, string, string)>.Failure(Error.Forbidden("Bu belgeye erişim yetkiniz yok."));

        try
        {
            var stream = _fileStorage.Read(doc.StoragePath);
            var contentType = doc.ContentType == "application/pdf"
                ? "application/pdf"
                : "application/octet-stream";
            return Result<(Stream, string, string)>.Success((stream, contentType, doc.FileName));
        }
        catch (FileNotFoundException)
        {
            return Result<(Stream, string, string)>.Failure(
                Error.NotFound("Dosya storage'da bulunamadı. Belgeyi yeniden yükleyin."));
        }
    }

    private async Task DeleteChunkImagesAsync(Guid docId, CancellationToken ct)
    {
        var chunks = await _uow.Chunks.FindAsync(c => c.DocumentId == docId, ct);
        foreach (var chunk in chunks)
        {
            if (string.IsNullOrWhiteSpace(chunk.ImagePath)) continue;
            try
            {
                var paths = JsonSerializer.Deserialize<List<string>>(chunk.ImagePath);
                if (paths is null) continue;
                foreach (var p in paths)
                    await _fileStorage.DeleteAsync(p, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[ImageCleanup] Resim silinemedi, chunk: {ChunkId}", chunk.Id);
            }
        }
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