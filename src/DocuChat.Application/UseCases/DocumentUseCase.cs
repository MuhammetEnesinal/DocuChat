
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Mapster;
using Microsoft.Extensions.Logging;
using DocuChat.Application.Interfaces.UseCases;
using DocuChat.Application.Interfaces.Services;
using DocuChat.Application.Interfaces.Repositories;
using DocuChat.Application.Common;
using DocuChat.Application.DTOs.Document;
using DocuChat.Domain.Entities;
using DocuChat.Domain.Enums;

namespace DocuChat.Application.UseCases;

public class DocumentUseCase : IDocumentUseCase
{
    private readonly IUnitOfWork _uow;
    private readonly IDocumentParser _parser;
    private readonly IEmbeddingService _embedder;
    private readonly IFileStorage _fileStorage;
    private readonly ICurrentUser _currentUser;
    private readonly IQuestionCacheRepository _cache;
    private readonly ILlmService _llm;
    private readonly ILogger<DocumentUseCase> _logger;

    public DocumentUseCase(
        IUnitOfWork uow,
        IDocumentParser parser,
        IEmbeddingService embedder,
        IFileStorage fileStorage,
        ICurrentUser currentUser,
        IQuestionCacheRepository cache,
        ILlmService llm,
        ILogger<DocumentUseCase> logger)
    {
        _uow = uow;
        _parser = parser;
        _embedder = embedder;
        _fileStorage = fileStorage;
        _currentUser = currentUser;
        _cache = cache;
        _llm = llm;
        _logger = logger;
    }

    // 1C: chunk içeriklerinden SHA256 hash üret. Reprocess'te değişir → cache mismatch.
    private static string ComputeContentHash(IEnumerable<DocumentChunk> chunks)
    {
        var combined = string.Join("\n", chunks.OrderBy(c => c.ChunkIndex).Select(c => c.Content));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(combined)));
    }

    // 4A: belgenin ilk birkaç chunk'ından LLM ile özet üret (best-effort, hata olursa null).
    private async Task<string?> TryGenerateSummaryAsync(IEnumerable<DocumentChunk> chunks, CancellationToken ct)
    {
        try
        {
            var sample = string.Join("\n", chunks
                .OrderBy(c => c.ChunkIndex)
                .Take(3)
                .Select(c => c.Content));
            return await _llm.GenerateDocumentSummaryAsync(sample, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Summary] Üretim başarısız, null kalacak");
            return null;
        }
    }

    public async Task<Result<DocumentResponseDto>> UploadAsync(
        UploadDocumentRequest req, CancellationToken ct)
    {
        // Duplicate check — aynı kullanıcı + aynı isim varsa disk'e bile yazma
        var alreadyExists = await _uow.Documents.ExistsByUserAndNameAsync(
            _currentUser.UserId, req.FileName, ct);
        if (alreadyExists)
        {
            return Result<DocumentResponseDto>.Failure(
                Error.Conflict($"'{req.FileName}' isimli bir belge zaten yüklü. Önce silin veya farklı isimde yükleyin."));
        }

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

            // 1C + 4A: ContentHash + Summary üret (sadece başarılı parse'da)
            doc.ContentHash = ComputeContentHash(addedChunks);
            doc.Summary = await TryGenerateSummaryAsync(addedChunks, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Parse/embed başarısız. DocId: {DocId}", doc.Id);
            doc.Status = DocumentStatus.Failed;
            doc.ErrorMessage = ex.Message;
            doc.UpdatedAt = DateTime.UtcNow;
            // Yarım kalan chunk'ları temizle
            var dirtyChunks = await _uow.Chunks.GetByDocumentIdAsync(doc.Id, ct);
            foreach (var c in dirtyChunks) _uow.Chunks.Delete(c);
        }

        await _uow.SaveChangesAsync(ct);
        return Result<DocumentResponseDto>.Success(doc.Adapt<DocumentResponseDto>());
    }

    public async Task<Result<IReadOnlyList<DocumentResponseDto>>> GetAllDocumentsAsync(
        string? search = null, CancellationToken ct = default)
    {
        // SQL-level search (ILIKE) — in-memory filter yerine
        var docs = await _uow.Documents.SearchAsync(search, ct);
        var dtos = docs.Select(d => d.Adapt<DocumentResponseDto>()).ToList();
        return Result<IReadOnlyList<DocumentResponseDto>>.Success(dtos);
    }

    public async Task<Result<PaginatedResult<DocumentResponseDto>>> GetAllDocumentsPagedAsync(
        int page, int pageSize, CancellationToken ct)
    {
        // SQL-level pagination — tüm belgeleri çekip in-memory paginate etmek yerine
        var paged = await _uow.Documents.GetPagedAsync(page, pageSize, search: null, ct);
        var dtos = paged.Items.Select(d => d.Adapt<DocumentResponseDto>()).ToList();
        return Result<PaginatedResult<DocumentResponseDto>>.Success(
            new PaginatedResult<DocumentResponseDto>(dtos, paged.TotalCount, paged.Page, paged.PageSize));
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

    public async Task<Result<IReadOnlyList<DocumentChunkResponseDto>>> GetChunksAsync(
        Guid id, CancellationToken ct)
    {
        var doc = await _uow.Documents.GetByIdAsync(id, ct);
        if (doc is null)
            return Result<IReadOnlyList<DocumentChunkResponseDto>>.Failure(Error.NotFound("Belge bulunamadı."));

        var chunks = await _uow.Chunks.GetByDocumentIdAsync(id, ct);
        var dtos = chunks
            .Select(c => c.Adapt<DocumentChunkResponseDto>())  // GetByDocumentIdAsync zaten ChunkIndex'e göre sıralı döner
            .ToList();

        return Result<IReadOnlyList<DocumentChunkResponseDto>>.Success(dtos);
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
            // 1. Yeni chunk'ları üret (henüz DB'ye yazma — parse fail olursa eski hali korunur)
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

            // 2. Parse başarılı — ARTIK eski chunk image dosyalarını DİSK'TEN sil
            // (Delete + Upload akışıyla aynı davranış — temiz yeniden işleme)
            await DeleteChunkImagesAsync(id, ct);

            // 3. Eski chunks DB'den sil + yenileri ekle (tek SaveChanges'te atomik)
            var existingChunks = await _uow.Chunks.GetByDocumentIdAsync(id, ct);
            foreach (var chunk in existingChunks)
                _uow.Chunks.Delete(chunk);
            foreach (var chunk in newChunks)
                await _uow.Chunks.AddAsync(chunk, ct);

            doc.Status = DocumentStatus.Ready;
            doc.ChunkCount = idx;
            doc.UpdatedAt = DateTime.UtcNow;

            // 1C + 4A: reprocess sonrası ContentHash + Summary güncelle
            doc.ContentHash = ComputeContentHash(newChunks);
            doc.Summary = await TryGenerateSummaryAsync(newChunks, ct);

            _logger.LogInformation("[Reprocess] Tamamlandı: {DocId} - {Count} chunk, NewHash={Hash}", id, idx, doc.ContentHash?[..8]);
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
        var chunks = await _uow.Chunks.GetByDocumentIdAsync(docId, ct);
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
        _ => throw new NotSupportedException($"Desteklenmeyen content type: {contentType}"),
    };
}
