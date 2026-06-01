
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using Mapster;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using DocuChat.Application.Interfaces.UseCases;
using DocuChat.Application.Interfaces.Services;
using DocuChat.Application.Interfaces.Repositories;
using DocuChat.Application.Common;
using DocuChat.Application.DTOs.Document;
using DocuChat.Domain.Entities;
using DocuChat.Domain.Enums;
using DocuChat.Application.ServiceContracts;

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
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ILogger<DocumentUseCase> _logger;
    private readonly bool _captionEnabled;
    private readonly int _captionMaxPerDoc;
    private readonly int _maxParallelChunks;
    private readonly int _commitBatchSize;

    public DocumentUseCase(
        IUnitOfWork uow,
        IDocumentParser parser,
        IEmbeddingService embedder,
        IFileStorage fileStorage,
        ICurrentUser currentUser,
        IQuestionCacheRepository cache,
        ILlmService llm,
        IServiceScopeFactory scopeFactory,
        IHostApplicationLifetime lifetime,
        IConfiguration cfg,
        ILogger<DocumentUseCase> logger)
    {
        _uow = uow;
        _parser = parser;
        _embedder = embedder;
        _fileStorage = fileStorage;
        _currentUser = currentUser;
        _cache = cache;
        _llm = llm;
        _scopeFactory = scopeFactory;
        _lifetime = lifetime;
        _logger = logger;
        _captionEnabled = cfg.GetValue<bool>("Caption:Enabled", false);
        _captionMaxPerDoc = cfg.GetValue<int>("Caption:MaxImagesPerDocument", 30);
        _maxParallelChunks = cfg.GetValue<int>("Chunking:MaxParallel", 2);
        _commitBatchSize = cfg.GetValue<int>("Chunking:CommitBatchSize", 50);
    }

    // Background fire-and-forget: yeni DI scope + ApplicationStopping token. HTTP request
    // lifecycle'ından bağımsız çalışır — browser timeout etse bile parsing devam eder.
    private void ScheduleBackgroundProcessing(Guid documentId)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var useCase = scope.ServiceProvider.GetRequiredService<IDocumentUseCase>();
                await useCase.ProcessPendingAsync(documentId, _lifetime.ApplicationStopping);
            }
            catch (OperationCanceledException) when (_lifetime.ApplicationStopping.IsCancellationRequested)
            {
                _logger.LogWarning("[BgProcess] {DocId} uygulama kapanırken iptal edildi", documentId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[BgProcess] {DocId} beklenmedik hata", documentId);
            }
        });
    }

    private async Task<List<DocumentChunk>> BuildChunksParallelAsync(
        Document doc,
        IReadOnlyList<ParsedChunk> parsedList,
        string earlySummary,
        CancellationToken ct)
    {
        var results = new DocumentChunk[parsedList.Count];
        using var sem = new SemaphoreSlim(_maxParallelChunks);
        var processed = 0;
        var total = parsedList.Count;

        _logger.LogInformation("[ChunkBuild] Başlatıldı — {Total} chunk, paralel={Par}", total, _maxParallelChunks);

        var tasks = parsedList.Select(async (parsed, idx) =>
        {
            await sem.WaitAsync(ct);
            try
            {
                var captions = await GenerateCaptionsAsync(parsed.ImagePath, parsed.Content, ct);
                // Header per-chunk → çok bölümlü dokümanlarda doğru bağlam (doc summary'den daha güvenilir)
                var chunkCtx = await _llm.GenerateChunkContextAsync(earlySummary, parsed.Header, parsed.Content, ct);

                // [IMG:N] tokenlarını [IMG:N — caption] formatına dönüştür (inline).
                var contentWithCaptions = captions.Count > 0
                    ? InlineImageCaptions(parsed.Content, captions)
                    : parsed.Content;

                // Embed için: temiz metin + bağlam + (inline caption'lı içerikten extract'lanan açıklamalar)
                var embedBase = !string.IsNullOrEmpty(parsed.CleanContent)
                    ? parsed.CleanContent!
                    : parsed.Content;
                var captionSummary = string.Join(" ", captions.Where(c => !string.IsNullOrWhiteSpace(c)));
                var embedText = embedBase;
                if (!string.IsNullOrEmpty(chunkCtx)) embedText = chunkCtx + " " + embedText;
                if (!string.IsNullOrEmpty(captionSummary)) embedText = embedText + " " + captionSummary;

                var contentSb = new StringBuilder();
                if (!string.IsNullOrEmpty(chunkCtx))
                    contentSb.Append("**[Bağlam]:** ").Append(chunkCtx).Append("\n\n");
                contentSb.Append(contentWithCaptions);

                var vec = await _embedder.GetEmbeddingAsync(embedText, ct);
                results[idx] = new DocumentChunk
                {
                    DocumentId = doc.Id,
                    Content = contentSb.ToString(),
                    ChunkIndex = idx,
                    Embedding = vec,
                    ImagePath = parsed.ImagePath,
                    Header = parsed.Header,
                    Summary = string.IsNullOrEmpty(captionSummary) ? null : captionSummary,
                    CleanContent = parsed.CleanContent,
                    PageNumber = parsed.PageNumber,
                    StructuredTableJson = parsed.StructuredTableJson,
                    TokenCount = parsed.TokenCount,
                    ContentHash = parsed.ContentHash,
                };
            }
            finally
            {
                sem.Release();
                var done = Interlocked.Increment(ref processed);
                // Her 10 chunk veya ilk + son için log
                if (done == 1 || done == total || done % 10 == 0)
                    _logger.LogInformation("[ChunkBuild] {Done}/{Total} chunk hazırlandı", done, total);
            }
        }).ToArray();

        await Task.WhenAll(tasks);

        var list = results.ToList();
        for (var i = 0; i < list.Count; i++)
        {
            list[i].PrevChunkId = i > 0 ? list[i - 1].Id : null;
            list[i].NextChunkId = i < list.Count - 1 ? list[i + 1].Id : null;
        }
        return list;
    }

    private async Task AddAndCommitInBatchesAsync(
        List<DocumentChunk> chunks, CancellationToken ct)
    {
        var batch = _commitBatchSize <= 0 ? chunks.Count : _commitBatchSize;
        var commitCount = 0;
        for (var i = 0; i < chunks.Count; i++)
        {
            await _uow.Chunks.AddAsync(chunks[i], ct);
            if ((i + 1) % batch == 0)
            {
                await _uow.SaveChangesAsync(ct);
                commitCount++;
                _logger.LogInformation("[Batch] {N}/{T} chunk commit edildi (batch #{B})",
                    i + 1, chunks.Count, commitCount);
            }
        }
        if (chunks.Count % batch != 0)
        {
            await _uow.SaveChangesAsync(ct);
            commitCount++;
            _logger.LogInformation("[Batch] {N}/{T} chunk commit edildi (batch #{B} — son)",
                chunks.Count, chunks.Count, commitCount);
        }
    }

    private async Task<List<string?>> GenerateCaptionsAsync(string? imagePathJson, string context, CancellationToken ct)
    {
        if (!_captionEnabled) return new();
        if (string.IsNullOrWhiteSpace(imagePathJson)) return new();

        List<string> paths;
        try
        {
            paths = JsonSerializer.Deserialize<List<string>>(imagePathJson) ?? new();
        }
        catch
        {
            return new();
        }

        if (paths.Count == 0) return new();

        // Quota koruması — chunk başına max N resim caption'lansın
        if (paths.Count > _captionMaxPerDoc)
            paths = paths.Take(_captionMaxPerDoc).ToList();

        // Caption üretmek index-aware: i. path için captions[i] string ya da null (üretilemedi).
        var captions = new string?[paths.Count];
        for (var i = 0; i < paths.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using var stream = _fileStorage.Read(paths[i]);
                using var ms = new MemoryStream();
                await stream.CopyToAsync(ms, ct);
                var bytes = ms.ToArray();
                if (bytes.Length < 64) continue;

                var mime = bytes[0] == 0xFF && bytes[1] == 0xD8 ? "image/jpeg"
                         : bytes[0] == 0x89 ? "image/png"
                         : "image/png";
                var caption = await _llm.GenerateImageCaptionAsync(bytes, mime, context, ct);
                if (!string.IsNullOrWhiteSpace(caption))
                    captions[i] = caption.Trim();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[Caption] {Path} için caption üretilemedi", paths[i]);
            }
        }

        return captions.ToList();
    }

    private static string InlineImageCaptions(string content, IReadOnlyList<string?> captions)
    {
        if (string.IsNullOrEmpty(content) || captions.Count == 0) return content;
        return System.Text.RegularExpressions.Regex.Replace(content, @"\[IMG:(\d+)\]", m =>
        {
            var n = int.Parse(m.Groups[1].Value);
            var idx = n - 1;
            if (idx < 0 || idx >= captions.Count) return m.Value;
            var caption = captions[idx];
            return string.IsNullOrWhiteSpace(caption) ? m.Value : $"[IMG:{n} — {caption}]";
        });
    }

    // Chunk içeriklerinden deterministik SHA256 — reprocess'te değişince cache invalidation tetiklenir.
    private static string ComputeContentHash(IEnumerable<DocumentChunk> chunks)
    {
        var combined = string.Join("\n", chunks.OrderBy(c => c.ChunkIndex).Select(c => c.Content));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(combined)));
    }

    // Belgenin ilk birkaç chunk'ından LLM ile 1-2 cümle özet (best-effort, hata olursa null).
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
        UploadDocumentRequestDto req, CancellationToken ct)
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
        try
        {
            await _uow.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (IsUniqueConstraintViolation(ex))
        {
            // DB unique index (UserId, FileName) — race koşulunda yakalanır (yukarıdaki check
            // ile aynı anda gelen ikinci istek). Disk'e yazılan dosyayı temizle.
            try { await _fileStorage.DeleteAsync(storagePath, CancellationToken.None); } catch { }
            return Result<DocumentResponseDto>.Failure(
                Error.Conflict($"'{req.FileName}' isimli bir belge zaten yüklü. Önce silin veya farklı isimde yükleyin."));
        }

        // Heavy lifting (parse + chunk + embed) HTTP path'inde değil — browser timeout'una düşer.
        // Scheduler fire-and-forget background task açar; bu metot anında döner.
        ScheduleBackgroundProcessing(doc.Id);
        _logger.LogInformation("[Upload] {DocId} kabul edildi (Status=Pending), background işleme zamanlandı", doc.Id);

        return Result<DocumentResponseDto>.Success(doc.Adapt<DocumentResponseDto>());
    }

    // PostgreSQL unique violation = SQLState 23505. EF Core DbUpdateException içine sarar.
    private static bool IsUniqueConstraintViolation(Exception ex)
    {
        var inner = ex.InnerException ?? ex;
        return inner.GetType().FullName == "Npgsql.PostgresException"
            && inner.GetType().GetProperty("SqlState")?.GetValue(inner) as string == "23505";
    }

    // Scheduler tarafından background scope'tan çağrılır. CT = application lifetime stopping.
    // Hem ilk upload (boş chunks) hem reprocess (mevcut chunks) için aynı yol — fark sadece
    // FINAL aşamada atomic swap yapılması (reprocess'te eski chunks korunur, yeni hazırken atılır).
    public async Task ProcessPendingAsync(Guid documentId, CancellationToken ct = default)
    {
        var doc = await _uow.Documents.GetByIdAsync(documentId, ct);
        if (doc is null)
        {
            _logger.LogWarning("[Process] {DocId} bulunamadı — atlandı", documentId);
            return;
        }
        if (doc.StoragePath is null)
        {
            _logger.LogWarning("[Process] {DocId} StoragePath yok — Failed işaretlendi", documentId);
            doc.Status = DocumentStatus.Failed;
            doc.ErrorMessage = "Storage path eksik";
            await SaveFinalStateAsync(doc.Id);
            return;
        }

        // Reprocess tespiti: chunks mevcutsa, build sırasında onları koruyup atomic swap yapacağız.
        var existingChunks = await _uow.Chunks.GetByDocumentIdAsync(documentId, ct);
        var isReprocess = existingChunks.Count > 0;

        doc.Status = DocumentStatus.Processing;
        doc.UpdatedAt = DateTime.UtcNow;
        await _uow.SaveChangesAsync(ct);

        try
        {
            using var stream = _fileStorage.Read(doc.StoragePath);
            var parsedList = (await _parser.ParseAsync(stream, doc.FileType)).ToList();

            // Contextual Retrieval: önce ilk 3 chunk'tan kısa belge özeti
            var earlySummary = await TryGenerateSummaryAsync(
                parsedList.Take(3).Select(c => new DocumentChunk { Content = c.Content }).ToList(), ct)
                ?? string.Empty;

            var newChunks = await BuildChunksParallelAsync(doc, parsedList, earlySummary, ct);

            if (isReprocess)
            {
                // ATOMIC SWAP: eski chunks delete + yeni chunks add + doc.Status=Ready hepsi tek
                // SaveChanges'te (EF Core tek transaction). Bu sayede reprocess süresince chat
                // eski chunks ile çalışmaya devam eder; swap anında yeni chunks aktif olur.
                foreach (var old in existingChunks)
                    _uow.Chunks.Delete(old);
                foreach (var chunk in newChunks)
                    await _uow.Chunks.AddAsync(chunk, ct);
                _logger.LogInformation("[Process] Reprocess atomic swap: {Old} eski → {New} yeni",
                    existingChunks.Count, newChunks.Count);
            }
            else
            {
                // İlk upload: batch commit (memory efficiency; chat zaten ilk uploadda hiç çalışmıyor)
                await AddAndCommitInBatchesAsync(newChunks, ct);
            }

            doc.Status = DocumentStatus.Ready;
            doc.ChunkCount = newChunks.Count;
            doc.UpdatedAt = DateTime.UtcNow;
            doc.ContentHash = ComputeContentHash(newChunks);
            doc.Summary = await TryGenerateSummaryAsync(newChunks, ct);

            await _cache.ClearAllAsync(ct);
            _logger.LogInformation("[Cache] Belge işleme tamamlandı — cache temizlendi. DocId: {DocId}", doc.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Process] Parse/embed başarısız. DocId: {DocId}", doc.Id);
            doc.Status = DocumentStatus.Failed;
            doc.ErrorMessage = ex.Message;
            doc.UpdatedAt = DateTime.UtcNow;

            // Reprocess'te eski chunks korunur (kullanıcı kayıp yaşamasın); ilk upload'da yarım kalanlar temizlenir.
            if (!isReprocess)
            {
                var dirtyChunks = await _uow.Chunks.GetByDocumentIdAsync(doc.Id, CancellationToken.None);
                foreach (var c in dirtyChunks) _uow.Chunks.Delete(c);
            }
        }

        // Final state mutlaka yazılmalı — uygulama kapanmak üzere olsa bile.
        // Aksi halde doc Processing'de kalır, sonraki başlatmada recovery hook Failed yapana kadar bozuk görünür.
        await SaveFinalStateAsync(doc.Id);
    }

    // CancellationToken.None ile final SaveChanges — Status=Ready/Failed muhakkak kaydedilmeli.
    private async Task SaveFinalStateAsync(Guid documentId)
    {
        try
        {
            await _uow.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Process] Final state save başarısız — doc Processing'de kalabilir. DocId: {DocId}", documentId);
        }
    }

    public async Task<Result<IReadOnlyList<DocumentResponseDto>>> GetAllDocumentsAsync(
        string? search = null, CancellationToken ct = default)
    {
        var docs = await _uow.Documents.SearchAsync(search, ct);
        var dtos = docs.Select(d => d.Adapt<DocumentResponseDto>()).ToList();
        return Result<IReadOnlyList<DocumentResponseDto>>.Success(dtos);
    }

    public async Task<Result<PaginatedResult<DocumentResponseDto>>> GetAllDocumentsPagedAsync(
        int page, int pageSize, CancellationToken ct)
    {
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
        var allDeletedImagePaths = new List<string>();
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
            var deletedForDoc = await DeleteChunkImagesAsync(id, ct);
            allDeletedImagePaths.AddRange(deletedForDoc);
            if (doc.StoragePath is not null)
                await _fileStorage.DeleteAsync(doc.StoragePath, ct);
            _uow.Documents.Delete(doc);
            deletedIds.Add(id);
        }

        if (deletedIds.Count > 0)
        {
            await _uow.SaveChangesAsync(ct);
            await _cache.ClearAllAsync(ct);
            await RemoveDeletedImagesFromChatHistoryAsync(allDeletedImagePaths, ct);
            await _uow.SaveChangesAsync(ct);
            _logger.LogInformation("[Batch] {Count} belge silindi, tüm soru cache'i temizlendi", deletedIds.Count);
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

        var deletedImagePaths = await DeleteChunkImagesAsync(docId, ct);
        if (doc.StoragePath is not null)
            await _fileStorage.DeleteAsync(doc.StoragePath, ct);

        _uow.Documents.Delete(doc);
        await _uow.SaveChangesAsync(ct);

        await _cache.ClearAllAsync(ct);
        await RemoveDeletedImagesFromChatHistoryAsync(deletedImagePaths, ct);
        await _uow.SaveChangesAsync(ct);
        _logger.LogInformation("[Cache] Belge silindi, tüm soru cache'i temizlendi. DocId: {DocId}", docId);

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

        // Çift tıklama / eşzamanlı istek koruması: zaten beklemede/işleniyor ise yeniden planlamadan dön.
        if (doc.Status == DocumentStatus.Pending || doc.Status == DocumentStatus.Processing)
        {
            _logger.LogInformation("[Reprocess] {DocId} zaten {Status} — yeni schedule atlandı", id, doc.Status);
            return Result<DocumentResponseDto>.Success(doc.Adapt<DocumentResponseDto>());
        }

        // Chunks SİLİNMİYOR — reprocess süresince eski chunk'larla chat çalışmaya devam eder.
        // Yeni chunks build edildikten sonra atomic swap yapılacak (ProcessPendingAsync içinde).
        doc.Status = DocumentStatus.Pending;
        doc.ErrorMessage = null;
        doc.UpdatedAt = DateTime.UtcNow;
        await _uow.SaveChangesAsync(ct);

        ScheduleBackgroundProcessing(id);
        _logger.LogInformation("[Reprocess] {DocId} background işleme zamanlandı (eski chunks korundu)", id);

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

    private async Task<List<string>> DeleteChunkImagesAsync(Guid docId, CancellationToken ct)
    {
        var deletedPaths = new List<string>();
        var chunks = await _uow.Chunks.GetByDocumentIdAsync(docId, ct);
        foreach (var chunk in chunks)
        {
            if (string.IsNullOrWhiteSpace(chunk.ImagePath)) continue;
            try
            {
                var paths = JsonSerializer.Deserialize<List<string>>(chunk.ImagePath);
                if (paths is null) continue;
                foreach (var p in paths)
                {
                    await _fileStorage.DeleteAsync(p, ct);
                    deletedPaths.Add(p);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[ImageCleanup] Resim silinemedi, chunk: {ChunkId}", chunk.Id);
            }
        }
        return deletedPaths;
    }

    private async Task RemoveDeletedImagesFromChatHistoryAsync(
        IReadOnlyCollection<string> deletedImagePaths, CancellationToken ct)
    {
        if (deletedImagePaths.Count == 0) return;
        try
        {
            var affected = await _uow.Messages.RemoveDeletedImagePathsAsync(deletedImagePaths, ct);
            if (affected > 0)
                _logger.LogInformation("[Cleanup] {N} chat mesajından {P} silinmiş resim referansı çıkarıldı",
                    affected, deletedImagePaths.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Cleanup] Chat geçmişi silinmiş resim temizliği başarısız");
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
