
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
    private readonly ITokenCounter _tokenCounter;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly IDocumentProcessingScheduler _scheduler;
    private readonly ILogger<DocumentUseCase> _logger;
    private readonly bool _captionEnabled;
    private readonly int _captionMaxPerDoc;
    private readonly int _captionMaxParallel;
    private readonly int _maxParallelChunks;
    private readonly int _commitBatchSize;
    private readonly int _chunkContextBatchTokenBudget;

    public DocumentUseCase(
        IUnitOfWork uow,
        IDocumentParser parser,
        IEmbeddingService embedder,
        IFileStorage fileStorage,
        ICurrentUser currentUser,
        IQuestionCacheRepository cache,
        ILlmService llm,
        ITokenCounter tokenCounter,
        IServiceScopeFactory scopeFactory,
        IHostApplicationLifetime lifetime,
        IDocumentProcessingScheduler scheduler,
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
        _tokenCounter = tokenCounter;
        _scopeFactory = scopeFactory;
        _lifetime = lifetime;
        _scheduler = scheduler;
        _logger = logger;
        _captionEnabled = cfg.GetValue<bool>("Caption:Enabled", false);
        _captionMaxPerDoc = cfg.GetValue<int>("Caption:MaxImagesPerDocument", 30);
        _captionMaxParallel = Math.Max(1, cfg.GetValue<int>("Caption:MaxParallel", 3));
        _maxParallelChunks = cfg.GetValue<int>("Chunking:MaxParallel", 2);
        _commitBatchSize = cfg.GetValue<int>("Chunking:CommitBatchSize", 50);
        // Mistral Small 32K context'in güvenli yarısı (output + prompt overhead için pay).
        _chunkContextBatchTokenBudget = cfg.GetValue<int>("ChunkContext:BatchTokenBudget", 18000);
    }

    // 🆕 Bounded Channel Queue üzerinden — DocumentProcessingConsumer paralel max N işler.
    // Task.Run+Scope eskisi gibi DEĞİL: SemaphoreSlim ile resource exhaustion'a karşı koruma.
    // Persistence: DocumentRecoveryService startup'ta Pending/Processing'i tekrar enqueue eder.
    private async ValueTask ScheduleBackgroundProcessingAsync(Guid documentId, CancellationToken ct = default)
    {
        await _scheduler.ScheduleAsync(documentId, ct);
        _logger.LogDebug("[BgProcess] {DocId} processing queue'sune eklendi", documentId);
    }

    // ChunkContext PARALEL BATCH üretimi — DYNAMIC token-budget batching.
    // Eski: sabit 10 chunk/batch → büyük chunk'larda taşma (Mistral Small 32K limit).
    // Yeni: chunk token sayısına göre batch oluştur → ortalama 18K input/batch.
    //   - Küçük chunk'lı belgelerde daha az batch (~3 vs 5) → cost ↓
    //   - Tablo yoğun belgelerde batch sayısı korunur → crash riski sıfır
    //   - Tek chunk bütçeyi aşarsa: kendi başına batch olur (LLM 32K limitine girer)
    // Failure: o batch için boş context (sistem normal çalışır).
    private async Task<string[]> ComputeChunkContextsAsync(
        IReadOnlyList<ParsedChunk> parsedList,
        string earlySummary,
        CancellationToken ct)
    {
        const int MaxParallelBatches = 5;  // Mistral Small rate-limit koruması
        var tokenBudget = _chunkContextBatchTokenBudget;

        var results = new string[parsedList.Count];

        // Token-budget tabanlı batch oluştur
        var batchInputs = new List<(int Start, List<ParsedChunk> Items)>();
        var currentBatch = new List<ParsedChunk>();
        var currentStart = 0;
        var currentTokens = 0;

        for (var i = 0; i < parsedList.Count; i++)
        {
            var chunkTokens = _tokenCounter.Count(parsedList[i].Content);

            // Bu chunk eklenirse limit aşılır → mevcut batch'i flush et
            if (currentTokens + chunkTokens > tokenBudget && currentBatch.Count > 0)
            {
                batchInputs.Add((currentStart, currentBatch));
                currentBatch = new List<ParsedChunk>();
                currentStart = i;
                currentTokens = 0;
            }

            currentBatch.Add(parsedList[i]);
            currentTokens += chunkTokens;
        }
        if (currentBatch.Count > 0)
            batchInputs.Add((currentStart, currentBatch));

        _logger.LogInformation(
            "[BatchCtx] {Total} chunk → {Batches} batch (token-budget {Budget}, max paralel {Par})",
            parsedList.Count, batchInputs.Count, tokenBudget, MaxParallelBatches);

        using var batchSem = new SemaphoreSlim(MaxParallelBatches);
        var batchTasks = batchInputs.Select(async batch =>
        {
            await batchSem.WaitAsync(ct);
            try
            {
                var inputs = batch.Items.Select(p => ((string?)p.Header, p.Content)).ToList();
                var contexts = await _llm.GenerateChunkContextsBatchAsync(earlySummary, inputs, ct);
                for (var j = 0; j < batch.Items.Count; j++)
                    results[batch.Start + j] = j < contexts.Count ? (contexts[j] ?? "") : "";
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[BatchCtx] Batch start={S} hatası — boş context", batch.Start);
                for (var j = 0; j < batch.Items.Count; j++)
                    results[batch.Start + j] = "";
            }
            finally { batchSem.Release(); }
        });
        await Task.WhenAll(batchTasks);

        return results;
    }

    /// <summary>
    /// CAPTION HASH-FIRST: Pixtral'a gitmeden önce tüm görsellerin SHA256 hash'i hesaplanır.
    /// Aynı hash → aynı görsel → Pixtral SADECE BİR KEZ çağrılır.
    /// Sonuç: path → caption sözlüğü + path → hash sözlüğü (chunk-level dedup için).
    /// </summary>
    private async Task<(Dictionary<string, string?> CaptionMap, Dictionary<string, string?> PathToHash)>
        PreComputeImageCaptionsAsync(
        IReadOnlyList<ParsedChunk> parsedList,
        string globalContext,
        CancellationToken ct)
    {
        var result = new Dictionary<string, string?>(StringComparer.Ordinal);
        var emptyHashes = new Dictionary<string, string?>(StringComparer.Ordinal);
        if (!_captionEnabled) return (result, emptyHashes);

        // [1] Tüm chunks'tan benzersiz path'leri topla
        var allPaths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var parsed in parsedList)
        {
            if (string.IsNullOrWhiteSpace(parsed.ImagePath)) continue;
            try
            {
                var paths = JsonSerializer.Deserialize<List<string>>(parsed.ImagePath) ?? new();
                foreach (var p in paths)
                    if (!string.IsNullOrWhiteSpace(p)) allPaths.Add(p);
            }
            catch { }
        }
        if (allPaths.Count == 0) return (result, emptyHashes);

        // [2] Belge başına quota — quota aşılırsa sessiz değil, log yansır
        var quotaExceeded = allPaths.Count > _captionMaxPerDoc;
        var limitedPaths = quotaExceeded
            ? allPaths.Take(_captionMaxPerDoc).ToList()
            : allPaths.ToList();
        if (quotaExceeded)
        {
            _logger.LogWarning(
                "[Caption][Quota] Belgede {Total} benzersiz görsel — quota {Limit} → {Skipped} görsel caption ALMAYACAK (chunk content'inde [IMG:N] marker'ları kalır, alt text boş gösterilir)",
                allPaths.Count, _captionMaxPerDoc, allPaths.Count - _captionMaxPerDoc);
        }

        // [3] Path → hash hesapla (paralel max 8 disk read)
        var pathToHash = new Dictionary<string, string?>(StringComparer.Ordinal);
        var hashSem = new SemaphoreSlim(8);
        var hashTasks = limitedPaths.Select(async path =>
        {
            await hashSem.WaitAsync(ct);
            try
            {
                var hash = await ComputeImageHashAsync(path, ct);
                lock (pathToHash) pathToHash[path] = hash;
            }
            finally { hashSem.Release(); }
        });
        await Task.WhenAll(hashTasks);

        // [4] Hash → temsilci path (aynı hash'i paylaşan path'lerden ilki)
        var hashToRepresentativePath = new Dictionary<string, string>(StringComparer.Ordinal);
        var pathsWithoutHash = new List<string>();
        foreach (var path in limitedPaths)
        {
            var hash = pathToHash[path];
            if (hash == null)
            {
                pathsWithoutHash.Add(path);  // hash hesaplanamadı → her birini ayrı çağır
                continue;
            }
            if (!hashToRepresentativePath.ContainsKey(hash))
                hashToRepresentativePath[hash] = path;
        }

        var uniqueImageCount = hashToRepresentativePath.Count + pathsWithoutHash.Count;
        var pixtralCallsSaved = limitedPaths.Count - uniqueImageCount;
        _logger.LogInformation(
            "[Caption] Hash dedup: {Total} görsel → {Unique} benzersiz, {Saved} Pixtral çağrısı atlanacak",
            limitedPaths.Count, uniqueImageCount, pixtralCallsSaved);

        // [5] Her benzersiz görsel için Pixtral çağrısı — paralel max _captionMaxParallel
        var hashToCaption = new Dictionary<string, string?>(StringComparer.Ordinal);
        using var captionSem = new SemaphoreSlim(_captionMaxParallel);

        var uniqueWork = hashToRepresentativePath
            .Select(kvp => (Key: kvp.Key, Path: kvp.Value, IsHashed: true))
            .Concat(pathsWithoutHash.Select(p => (Key: p, Path: p, IsHashed: false)));

        var captionTasks = uniqueWork.Select(async work =>
        {
            await captionSem.WaitAsync(ct);
            try
            {
                var caption = await GenerateSingleCaptionAsync(work.Path, globalContext, ct);
                if (work.IsHashed)
                    lock (hashToCaption) hashToCaption[work.Key] = caption;
                else
                    lock (result) result[work.Path] = caption;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[Caption] {Path} için caption üretilemedi", work.Path);
            }
            finally { captionSem.Release(); }
        });
        await Task.WhenAll(captionTasks);

        // [6] path → caption sözlüğü oluştur (hash üzerinden hepsi aynı caption'ı paylaşır)
        foreach (var path in limitedPaths)
        {
            if (result.ContainsKey(path)) continue;  // pathsWithoutHash zaten yazıldı
            var hash = pathToHash[path];
            result[path] = hash != null && hashToCaption.TryGetValue(hash, out var cap) ? cap : null;
        }

        return (result, pathToHash);
    }

    // Tek bir görseli Pixtral'a gönderip caption al. Hata olursa null döner.
    private async Task<string?> GenerateSingleCaptionAsync(string path, string context, CancellationToken ct)
    {
        try
        {
            using var stream = _fileStorage.Read(path);
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms, ct);
            var bytes = ms.ToArray();
            if (bytes.Length < 64) return null;

            var mime = bytes[0] == 0xFF && bytes[1] == 0xD8 ? "image/jpeg"
                     : bytes[0] == 0x89 ? "image/png"
                     : "image/png";
            var caption = await _llm.GenerateImageCaptionAsync(bytes, mime, context, ct);
            return string.IsNullOrWhiteSpace(caption) ? null : caption.Trim();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[Caption] {Path} okuma/Pixtral hatası", path);
            return null;
        }
    }

    // Chunk paralel build sırasında per-chunk caption listesi topla (DocumentImages için lazım).
    // captionMap: PreComputeImageCaptionsAsync'ten gelen path → caption sözlüğü.
    // pathToHash: chunk-level path dedup için (aynı ContentHash'li PdfPig+Mistral path'leri tek IMG marker'a indir).
    private async Task<(List<DocumentChunk> Chunks, List<string?>[] CaptionsByChunk)>
        BuildChunksAndCollectCaptionsAsync(
        Document doc,
        IReadOnlyList<ParsedChunk> parsedList,
        string earlySummary,
        IReadOnlyDictionary<string, string?> captionMap,
        IReadOnlyDictionary<string, string?> pathToHash,
        CancellationToken ct)
    {
        var results = new DocumentChunk[parsedList.Count];
        var captionsByChunk = new List<string?>[parsedList.Count];  // index → caption listesi
        using var sem = new SemaphoreSlim(_maxParallelChunks);
        var processed = 0;
        var total = parsedList.Count;

        _logger.LogInformation("[ChunkBuild] Başlatıldı — {Total} chunk, paralel={Par}", total, _maxParallelChunks);

        // Pre-compute: tüm chunk context'leri paralel batch (50 chunk → 5 batch ≈ 10sn)
        var precomputedContexts = await ComputeChunkContextsAsync(parsedList, earlySummary, ct);

        var tasks = parsedList.Select(async (parsed, idx) =>
        {
            await sem.WaitAsync(ct);
            try
            {
                // CHUNK-LEVEL PATH DEDUP: PdfPig + Mistral aynı görseli iki kez yakalamış olabilir.
                // ContentHash'i aynı olan path'lerin [IMG:N] markerlarını tek marker'a indir.
                // Sonuç: parsed.Content'in renumber edilmiş hâli + dedup edilmiş path listesi.
                var (dedupContent, dedupPaths) = DedupChunkPaths(
                    parsed.Content, parsed.ImagePath, pathToHash);

                // Caption lookup dedup edilmiş path listesi üzerinden
                var captions = dedupPaths.Select(p =>
                    captionMap.TryGetValue(p, out var c) ? c : null).ToList();
                captionsByChunk[idx] = captions;

                // Batch'ten gelen context — LLM call yok, sözlük lookup
                var chunkCtx = precomputedContexts[idx] ?? string.Empty;

                // [IMG:N] tokenlarını [IMG:N — caption] formatına dönüştür (inline).
                var contentWithCaptions = captions.Count > 0
                    ? InlineImageCaptions(dedupContent, captions)
                    : dedupContent;

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

                // 🆕 EMBEDDING RETRY: 3 deneme exponential backoff. Final fail olursa
                // o chunk null'lanır → işlem sonunda filter edilir, belge yine Ready olur.
                // Bir chunk fail = tüm belgenin fail olması ENGELLENİR.
                float[]? vec = null;
                for (var attempt = 1; attempt <= 3; attempt++)
                {
                    try
                    {
                        vec = await _embedder.GetEmbeddingAsync(embedText, ct);
                        break;
                    }
                    catch (Exception ex) when (attempt < 3 && !ct.IsCancellationRequested)
                    {
                        _logger.LogWarning(ex,
                            "[ChunkBuild] Chunk #{Idx} embedding hatası (attempt {N}/3) — retry",
                            idx, attempt);
                        await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt - 1)), ct);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex,
                            "[ChunkBuild] Chunk #{Idx} embedding 3 denemede de başarısız — chunk ATLANACAK",
                            idx);
                        // vec null kalır → aşağıdaki check ile sentinel kaydedilmez
                    }
                }

                if (vec is null)
                {
                    // Embedding alınamadı — chunk results[idx] null kalır, filter edilir
                    return;
                }

                results[idx] = new DocumentChunk
                {
                    DocumentId = doc.Id,
                    Content = contentSb.ToString(),
                    ChunkIndex = idx,
                    Embedding = vec,
                    // ImagePath kaldırıldı — görseller artık DocumentImages + ChunkImages join'de
                    Header = parsed.Header,
                    Summary = string.IsNullOrEmpty(captionSummary) ? null : captionSummary,
                    CleanContent = parsed.CleanContent,
                    PageNumber = parsed.PageNumber,
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

        // 🆕 Null chunk'ları (embedding fail olanlar) filtrele + uyarı logu
        var validChunks = results.Where(c => c != null).ToList();
        var skipped = total - validChunks.Count;
        if (skipped > 0)
        {
            _logger.LogWarning(
                "[ChunkBuild] {Skipped}/{Total} chunk embedding hatası nedeniyle atlandı — belge yine de Ready olacak",
                skipped, total);
        }

        // captionsByChunk array'inin filtrelenmiş indeks'lere göre yeniden hizalanması gerekiyor
        var alignedCaptions = new List<string?>[validChunks.Count];
        var newIdx = 0;
        for (var oldIdx = 0; oldIdx < results.Length; oldIdx++)
        {
            if (results[oldIdx] is null) continue;
            alignedCaptions[newIdx++] = captionsByChunk[oldIdx] ?? new List<string?>();
        }

        // Prev/Next zincirleme link
        for (var i = 0; i < validChunks.Count; i++)
        {
            validChunks[i].ChunkIndex = i; // re-index (atlanan chunks için)
            validChunks[i].PrevChunkId = i > 0 ? validChunks[i - 1].Id : null;
            validChunks[i].NextChunkId = i < validChunks.Count - 1 ? validChunks[i + 1].Id : null;
        }
        return (validChunks, alignedCaptions);
    }

    // captionMap'ten chunk'a ait path'lerin caption'larını sıralı döndürür.
    private static List<string?> LookupCaptionsFromMap(
        string? imagePathJson, IReadOnlyDictionary<string, string?> captionMap)
    {
        if (string.IsNullOrWhiteSpace(imagePathJson)) return new List<string?>();
        try
        {
            var paths = JsonSerializer.Deserialize<List<string>>(imagePathJson) ?? new();
            return paths.Select(p => captionMap.TryGetValue(p, out var c) ? c : null).ToList();
        }
        catch
        {
            return new List<string?>();
        }
    }

    /// <summary>
    /// Chunk içinde aynı ContentHash'li path'leri (PdfPig + Mistral aynı görseli yakaladıysa)
    /// tek marker'a indirir:
    ///   1. parsed.ImagePath JSON'unu paths listesine deserialize et
    ///   2. Her path için pathToHash lookup ile hash al; aynı hash → aynı canonical index
    ///   3. Content içinde [IMG:N] markerlarını renumber: duplicate'ler ilk occurrence'a yönlendirilir
    ///   4. Sonuç: dedup edilmiş content + paths listesi
    /// Hash bilinmiyorsa (limit aşımı veya hesaplanamamış) path unique kabul edilir (kayıp riski yok).
    /// </summary>
    private static (string DedupedContent, List<string> DedupedPaths) DedupChunkPaths(
        string content,
        string? imagePathJson,
        IReadOnlyDictionary<string, string?> pathToHash)
    {
        if (string.IsNullOrWhiteSpace(imagePathJson))
            return (content ?? string.Empty, new List<string>());

        List<string> paths;
        try { paths = JsonSerializer.Deserialize<List<string>>(imagePathJson) ?? new(); }
        catch { return (content ?? string.Empty, new List<string>()); }

        if (paths.Count <= 1) return (content ?? string.Empty, paths);

        // oldIdx → newIdx mapping (hash bazlı dedup)
        var hashToNewIdx = new Dictionary<string, int>(StringComparer.Ordinal);
        var oldToNewIdx = new int[paths.Count];
        var dedupPaths = new List<string>();

        for (var i = 0; i < paths.Count; i++)
        {
            var path = paths[i];
            pathToHash.TryGetValue(path, out var hash);

            if (!string.IsNullOrEmpty(hash) && hashToNewIdx.TryGetValue(hash, out var existingIdx))
            {
                oldToNewIdx[i] = existingIdx;  // duplicate
            }
            else
            {
                oldToNewIdx[i] = dedupPaths.Count;
                dedupPaths.Add(path);
                if (!string.IsNullOrEmpty(hash)) hashToNewIdx[hash] = oldToNewIdx[i];
            }
        }

        // Renumber yapılmadıysa (hiç duplicate yok) content'i değiştirme
        if (dedupPaths.Count == paths.Count) return (content ?? string.Empty, paths);

        // Content içinde [IMG:N] markerlarını yeni indexlerle değiştir
        var newContent = System.Text.RegularExpressions.Regex.Replace(
            content ?? string.Empty,
            @"\[IMG:(\d+)([^\]]*)\]",
            m =>
            {
                if (!int.TryParse(m.Groups[1].Value, out var oldNum)) return m.Value;
                var oldIdx = oldNum - 1;
                if (oldIdx < 0 || oldIdx >= oldToNewIdx.Length) return m.Value;
                var newIdx = oldToNewIdx[oldIdx];
                return $"[IMG:{newIdx + 1}{m.Groups[2].Value}]";
            });

        return (newContent, dedupPaths);
    }

    /// <summary>
    /// DocumentImage + ChunkImage entity'lerini chunks'lardan inşa eder.
    /// Görsel path'leri parsedList'ten alınır (chunk.ImagePath artık kaldırıldı).
    /// İki seviye dedupe:
    ///   1. PATH (aynı dosya path'i → aynı image)
    ///   2. CONTENT HASH (farklı path ama aynı byte içerik → aynı image)
    /// </summary>
    private async Task PersistImagesAndLinksAsync(
        Document doc,
        IReadOnlyList<DocumentChunk> chunks,
        IReadOnlyList<ParsedChunk> parsedList,
        IReadOnlyList<List<string?>> captionsByChunk,
        CancellationToken ct)
    {
        var pathToImage = new Dictionary<string, DocumentImage>(StringComparer.Ordinal);
        var hashToImage = new Dictionary<string, DocumentImage>(StringComparer.Ordinal);
        var newImages = new List<DocumentImage>();
        var newLinks = new List<ChunkImage>();
        var dedupHits = 0;

        for (var ci = 0; ci < chunks.Count; ci++)
        {
            var chunk = chunks[ci];
            var parsed = parsedList[ci];  // path source artık parsed
            if (string.IsNullOrWhiteSpace(parsed.ImagePath)) continue;

            List<string> paths;
            try { paths = JsonSerializer.Deserialize<List<string>>(parsed.ImagePath) ?? new(); }
            catch { continue; }
            if (paths.Count == 0) continue;

            var captions = ci < captionsByChunk.Count ? captionsByChunk[ci] : null;

            for (var localIdx = 0; localIdx < paths.Count; localIdx++)
            {
                var path = paths[localIdx];
                if (string.IsNullOrWhiteSpace(path)) continue;

                // 1. Path eşleşmesi — anlık (disk okuma yok)
                if (pathToImage.TryGetValue(path, out var image))
                {
                    dedupHits++;
                    newLinks.Add(MakeLink(chunk.Id, image.Id, localIdx + 1));
                    continue;
                }

                // 2. Content hash hesapla
                var hash = await ComputeImageHashAsync(path, ct);

                // 3. Bu belge içinde aynı hash başka path olarak var mı?
                if (hash != null && hashToImage.TryGetValue(hash, out var existingByHash))
                {
                    dedupHits++;
                    pathToImage[path] = existingByHash;  // path → mevcut image
                    newLinks.Add(MakeLink(chunk.Id, existingByHash.Id, localIdx + 1));
                    continue;
                }

                // 4. Yeni görsel — kaydet
                var caption = (captions != null && localIdx < captions.Count) ? captions[localIdx] : null;
                image = new DocumentImage
                {
                    DocumentId = doc.Id,
                    Path = path,
                    Caption = string.IsNullOrWhiteSpace(caption) ? null : caption,
                    PageNumber = chunk.PageNumber,
                    Source = DetectImageSource(path),
                    ContentHash = hash,  // dedup için kullanılıyor
                };
                pathToImage[path] = image;
                if (hash != null) hashToImage[hash] = image;
                newImages.Add(image);

                newLinks.Add(MakeLink(chunk.Id, image.Id, localIdx + 1));
            }
        }

        foreach (var img in newImages) await _uow.Images.AddAsync(img, ct);
        foreach (var link in newLinks) await _uow.ChunkImages.AddAsync(link, ct);

        _logger.LogInformation(
            "[Images] {ImgCount} unique görsel + {LinkCount} chunk-link kayıt edildi ({Dedup} dedupe hit)",
            newImages.Count, newLinks.Count, dedupHits);
    }

    private static ChunkImage MakeLink(Guid chunkId, Guid imageId, int position) =>
        new() { ChunkId = chunkId, ImageId = imageId, PositionInChunk = position };

    // Görsel byte'larından SHA256 hash — belge içi content-based dedup için.
    // Hata olursa null döner (dedup atlanır, image yine kaydedilir).
    private async Task<string?> ComputeImageHashAsync(string path, CancellationToken ct)
    {
        try
        {
            using var stream = _fileStorage.Read(path);
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms, ct);
            var bytes = ms.ToArray();
            if (bytes.Length < 64) return null;
            var hash = SHA256.HashData(bytes);
            return Convert.ToHexString(hash);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[Images] {Path} hash hesaplanamadı — dedupe atlanır", path);
            return null;
        }
    }

    private static string DetectImageSource(string path)
    {
        var lower = path.ToLowerInvariant();
        if (lower.Contains("pdfpig")) return "PdfPig";
        if (lower.Contains("xlsx") || lower.Contains("excel")) return "Xlsx";
        return "Mistral";  // varsayılan
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

    // Belgenin BAŞ + ORTA + SON chunk'larından LLM ile 1-2 cümle özet (best-effort).
    // İlk 3 chunk yerine: ilk 2 + orta 2 + son 2 sample → TOC/kapak sayfasında saplanmaz,
    // gerçek içeriği temsil eder. Anthropic Contextual Retrieval için daha iyi globalContext.
    private async Task<string?> TryGenerateSummaryAsync(IEnumerable<DocumentChunk> chunks, CancellationToken ct)
    {
        try
        {
            var ordered = chunks.OrderBy(c => c.ChunkIndex).ToList();
            var sample = BuildSpreadSample(ordered, perRegion: 2);
            return await _llm.GenerateDocumentSummaryAsync(sample, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Summary] Üretim başarısız, null kalacak");
            return null;
        }
    }

    /// <summary>
    /// Belgenin BAŞ/ORTA/SON bölgelerinden N chunk seçip birleştirir.
    /// Kısa belgelerde (≤ perRegion×3) tüm chunk'lar kullanılır.
    /// </summary>
    private static string BuildSpreadSample(IReadOnlyList<DocumentChunk> chunks, int perRegion)
    {
        if (chunks.Count == 0) return string.Empty;
        if (chunks.Count <= perRegion * 3)
            return string.Join("\n", chunks.Select(c => c.Content));

        var selected = new List<DocumentChunk>();
        selected.AddRange(chunks.Take(perRegion));                                   // ilk N
        var midStart = (chunks.Count - perRegion) / 2;
        for (var i = 0; i < perRegion; i++) selected.Add(chunks[midStart + i]);     // orta N
        selected.AddRange(chunks.Skip(chunks.Count - perRegion));                    // son N
        return string.Join("\n", selected.Select(c => c.Content));
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
        // Scheduler queue'ya ekler, consumer paralel max N belge işler.
        await ScheduleBackgroundProcessingAsync(doc.Id, ct);
        _logger.LogInformation("[Upload] {DocId} kabul edildi (Status=Pending), processing queue'sune eklendi", doc.Id);

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

            // Contextual Retrieval: BAŞ + ORTA + SON chunk'lardan özet (TOC/kapak sayfasına saplanmaz)
            var sampleChunks = parsedList
                .Select((c, idx) => new DocumentChunk { Content = c.Content, ChunkIndex = idx })
                .ToList();
            var earlySummary = await TryGenerateSummaryAsync(sampleChunks, ct) ?? string.Empty;

            // CAPTION HASH-FIRST: Aynı görsel için Pixtral SADECE BİR KEZ çağrılır.
            // Hem caption hem pathToHash döner — pathToHash chunk-level path dedup için kullanılır.
            var (captionMap, pathToHash) = await PreComputeImageCaptionsAsync(parsedList, earlySummary, ct);

            // Yeni: per-chunk captions da topla → DocumentImage caption'ları için
            var (newChunks, captionsByChunk) = await BuildChunksAndCollectCaptionsAsync(
                doc, parsedList, earlySummary, captionMap, pathToHash, ct);

            // Reprocess'te silinecek eski image path'leri DB save BAŞARILI olduktan sonra
            // diskten silinecek (önce silersek SaveChanges fail ederse broken references kalır).
            List<string> pendingDiskDeletes = new();

            if (isReprocess)
            {
                // ATOMIC SWAP: eski chunks (ve cascade ile eski ChunkImages) + eski DocumentImages
                // silinir; yenileri eklenir. Hepsi tek SaveChanges → reprocess sırasında chat
                // eski chunks ile çalışmaya devam eder, swap anında yeni chunks aktif olur.
                var existingImages = await _uow.Images.GetByDocumentIdAsync(doc.Id, ct);

                // 🆕 DİSK SİLMEYİ ERTELE — SaveChanges başarılı olduktan sonra silinecek.
                // (Eskiden: önce disk silinirdi, SaveChanges fail ederse DB path'leri orphan kalırdı)
                foreach (var img in existingImages)
                {
                    if (!string.IsNullOrWhiteSpace(img.Path))
                        pendingDiskDeletes.Add(img.Path);
                }

                foreach (var old in existingChunks) _uow.Chunks.Delete(old);
                foreach (var img in existingImages) _uow.Images.Delete(img);
                foreach (var chunk in newChunks) await _uow.Chunks.AddAsync(chunk, ct);
                await PersistImagesAndLinksAsync(doc, newChunks, parsedList, captionsByChunk, ct);
                _logger.LogInformation(
                    "[Process] Reprocess: {OldC}→{NewC} chunk, {OldI} görsel DB'den silindi (disk cleanup SaveChanges sonrası)",
                    existingChunks.Count, newChunks.Count, existingImages.Count);
            }
            else
            {
                // İlk upload: batch commit (memory efficiency; chat zaten ilk uploadda hiç çalışmıyor)
                await AddAndCommitInBatchesAsync(newChunks, ct);
                // Görsel + link'ler ayrıca yazılır (chunks committed, ID'ler var)
                await PersistImagesAndLinksAsync(doc, newChunks, parsedList, captionsByChunk, ct);
            }

            doc.Status = DocumentStatus.Ready;
            doc.ChunkCount = newChunks.Count;
            doc.UpdatedAt = DateTime.UtcNow;
            doc.Summary = await TryGenerateSummaryAsync(newChunks, ct);

            // 🆕 ÖNCE DB save — eğer fail ederse disk dosyaları korunur (broken refs YOK)
            await _uow.SaveChangesAsync(ct);

            // 🆕 DB başarıyla commit edildi → orphan disk dosyalarını şimdi sil
            if (pendingDiskDeletes.Count > 0)
            {
                var deletedDiskCount = 0;
                foreach (var path in pendingDiskDeletes)
                {
                    try { await _fileStorage.DeleteAsync(path, ct); deletedDiskCount++; }
                    catch (Exception ex) { _logger.LogWarning(ex, "[Reprocess] Eski görsel silinemedi: {Path}", path); }
                }
                _logger.LogInformation(
                    "[Process] Reprocess disk cleanup: {DiskN}/{Total} eski görsel silindi",
                    deletedDiskCount, pendingDiskDeletes.Count);
            }

            // 🆕 PER-DOCUMENT CACHE INVALIDATION:
            //   - İlk upload: chunks DAHA ÖNCE cache'e refere edilemezdi (belge yeniydi) → temizleme GEREKMEZ
            //   - Reprocess: bu belgenin chunks'larıyla cevap üretmiş entries silinmeli + untracked (eski) fallback
            if (isReprocess)
            {
                var deletedCacheCount = await _cache.DeleteByDocumentIdAsync(doc.Id, includeUntracked: true, ct);
                _logger.LogInformation(
                    "[Cache] Reprocess sonrası belge-bazlı invalidation: {Count} cache entry silindi. DocId: {DocId}",
                    deletedCacheCount, doc.Id);
            }
            else
            {
                _logger.LogInformation(
                    "[Cache] İlk upload — cache temizleme atlandı (yeni belge eski cache'i invalid etmez). DocId: {DocId}",
                    doc.Id);
            }
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
            // 🆕 Per-document invalidation: sadece silinen belge ID'lerini içeren cache entries
            // (eski untracked entries de güvenlik için silinir — geriye uyumluluk)
            var totalCacheDeleted = 0;
            foreach (var delId in deletedIds)
                totalCacheDeleted += await _cache.DeleteByDocumentIdAsync(delId, includeUntracked: true, ct);
            await RemoveDeletedImagesFromChatHistoryAsync(allDeletedImagePaths, ct);
            await _uow.SaveChangesAsync(ct);
            _logger.LogInformation(
                "[Batch] {Count} belge silindi, {CacheN} cache entry invalidate edildi",
                deletedIds.Count, totalCacheDeleted);
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

        // 🆕 Per-document invalidation
        var deletedCacheCount = await _cache.DeleteByDocumentIdAsync(docId, includeUntracked: true, ct);
        await RemoveDeletedImagesFromChatHistoryAsync(deletedImagePaths, ct);
        await _uow.SaveChangesAsync(ct);
        _logger.LogInformation(
            "[Cache] Belge silindi, {Count} cache entry invalidate edildi. DocId: {DocId}",
            deletedCacheCount, docId);

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

        await ScheduleBackgroundProcessingAsync(id, ct);
        _logger.LogInformation("[Reprocess] {DocId} processing queue'sune eklendi (eski chunks korundu)", id);

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
        // Yeni mimari: DocumentImages tablosundan diskteki path'leri al, dedup zaten kayıtta var.
        var images = await _uow.Images.GetByDocumentIdAsync(docId, ct);
        foreach (var img in images)
        {
            if (string.IsNullOrWhiteSpace(img.Path)) continue;
            try
            {
                await _fileStorage.DeleteAsync(img.Path, ct);
                deletedPaths.Add(img.Path);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[ImageCleanup] Resim silinemedi: {Path}", img.Path);
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
