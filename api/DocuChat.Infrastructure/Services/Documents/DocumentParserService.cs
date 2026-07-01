using System.Diagnostics;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using DocuChat.Application.Common.Imaging;
using DocuChat.Application.Interfaces.Services;
using DocuChat.Domain.Enums;
using DocuChat.Infrastructure.Services.Documents.Parsing.Ast;
using DocuChat.Infrastructure.Services.Documents.Parsing.Chunking;
using DocuChat.Infrastructure.Services.Documents.Parsing.Linking;
using DocuChat.Infrastructure.Services.Documents.Parsing.Models;
using DocuChat.Infrastructure.Services.Documents.Parsing.Rendering;
using DocuChat.Infrastructure.Services.Documents.Parsing.StructuredExtractors;
using DocuChat.Application.ServiceContracts;
using DocumentFormat.OpenXml.Packaging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using UglyToad.PdfPig;
using A = DocumentFormat.OpenXml.Drawing;

namespace DocuChat.Infrastructure.Services.Documents;

public class DocumentParserService : IDocumentParser
{
    // Hot-path regex'ler — JIT-compiled, cached. Belge başına onlarca call.
    private static readonly Regex EmbedImgPlaceholderRegex =
        new(@"\[\[EMBED_IMG_(\d+)\]\]", RegexOptions.Compiled);

    private readonly IHttpClientFactory _httpFactory;
    private readonly IFileStorage _fileStorage;
    private readonly ILogger<DocumentParserService> _logger;
    private readonly string _mistralModel;
    private readonly int _maxTokens;
    private readonly string _sofficePath;

    // Adaptive token budget — belgenin paragraf dağılımına göre dinamik chunk boyutu
    private readonly bool _adaptiveEnabled;
    private readonly double _adaptiveMultiplier;
    private readonly int _adaptiveMin;
    private readonly int _adaptiveMax;

    // Chunking mode: "PageBased" (basit, Mistral ham markdown) | "Semantic" (AST + coalescer'lar)
    private readonly string _chunkingMode;

    private readonly ITokenCounter _tokens;
    private readonly MarkdownBlockExtractor _blockExtractor;
    private readonly BlockMerger _blockMerger;
    private readonly TableCoalescer _tableCoalescer;
    private readonly BlockCoalescer _blockCoalescer;
    private readonly ImageLinker _imageLinker;
    private readonly IMarkdownRenderer _renderer;
    private readonly SemanticChunker _chunker;
    private readonly PageBasedChunker _pageChunker;
    private readonly HierarchicalChunker _hierarchicalChunker;

    public DocumentParserService(
        IConfiguration cfg,
        IFileStorage fileStorage,
        IHttpClientFactory httpFactory,
        IEmbeddingService embedder,
        ILogger<DocumentParserService> logger)
    {
        _httpFactory = httpFactory;
        _fileStorage = fileStorage;
        _logger = logger;
        _mistralModel = cfg["Mistral:Model"] ?? "mistral-ocr-latest";
        _maxTokens = int.TryParse(cfg["Chunking:MaxTokens"], out var mt) ? mt : 800;
        _sofficePath = cfg["LibreOffice:Path"] ?? "soffice";

        // Adaptive token budget config (varsayılan: enabled, median × 2.5, 400-1500 clamp)
        _adaptiveEnabled = cfg.GetValue<bool>("Chunking:AdaptiveEnabled", true);
        _adaptiveMultiplier = cfg.GetValue<double>("Chunking:AdaptiveMultiplier", 2.5);
        _adaptiveMin = cfg.GetValue<int>("Chunking:AdaptiveMinTokens", 400);
        _adaptiveMax = cfg.GetValue<int>("Chunking:AdaptiveMaxTokens", 1500);
        // appsettings.json default'u "Semantic" — constructor fallback de onunla hizalı.
        _chunkingMode = cfg.GetValue<string>("Chunking:Mode", "Semantic") ?? "Semantic";

        var maxAtomicTokens = cfg.GetValue<int>("Chunking:MaxAtomicTokens", 1500);
        var blockCoalescerMin = cfg.GetValue<int>("Chunking:BlockCoalescer:MinTokens", 80);
        var blockCoalescerMax = cfg.GetValue<int>("Chunking:BlockCoalescer:MaxMergedTokens", 400);

        _tokens = new TokenCounter();
        _blockExtractor = new MarkdownBlockExtractor(new TableExtractor());
        _blockMerger = new BlockMerger();
        _tableCoalescer = new TableCoalescer();
        _blockCoalescer = new BlockCoalescer(_tokens, blockCoalescerMin, blockCoalescerMax);
        _imageLinker = new ImageLinker();
        _renderer = new MarkdownRenderer();
        var splitter = new SemanticSplitter(embedder, _tokens);
        _chunker = new SemanticChunker(_tokens, _renderer, splitter, maxAtomicTokens);
        _pageChunker = new PageBasedChunker(_tokens, _adaptiveMax);
        _hierarchicalChunker = new HierarchicalChunker(_tokens, _renderer, _adaptiveMax);

        // ClosedXML / MimeKit windows-1254 vb code page'ler için. Idempotent ama belge başına
        // çağırmak gereksiz — service singleton/scoped lifetime'da burada bir kez yeter.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public async Task<IEnumerable<ParsedChunk>> ParseAsync(Stream stream, FileType fileType, CancellationToken ct = default)
    {
        var bytes = await ReadAllBytesAsync(stream, ct);

        // DOCX / DOC: LibreOffice ile PDF'e çevir → PDF akışına yönlendir
        if (fileType is FileType.Docx or FileType.Doc)
        {
            var ext = fileType == FileType.Docx ? "docx" : "doc";
            if (IsMhtml(bytes))
            {
                _logger.LogInformation("[Parser] MHTML formatı tespit edildi → MimeKit ile HTML'e çıkar");
                bytes = await ExtractHtmlFromMhtmlAsync(bytes);
                ext = "html";
            }
            bytes = await ConvertToPdfAsync(bytes, ext, ct);
            fileType = FileType.Pdf;
        }

        var mime = MimeFor(fileType);

        // XLSX: belgeye [[EMBED_IMG_N]] placeholder'ları enjekte et, Mistral metne çevirsin.
        // Mistral fail olursa placeholder dosyalarını diske orphan bırakma → try/catch cleanup.
        List<string> placeholderPaths = new();
        if (fileType == FileType.Xlsx)
        {
            var prep = await PrepareXlsxWithPlaceholdersAsync(bytes);
            bytes = prep.Bytes;
            placeholderPaths = prep.Paths;
        }

        List<MistralPage> mistralPages;
        try
        {
            mistralPages = await CallMistralAsync(bytes, mime, ct);
        }
        catch
        {
            if (placeholderPaths.Count > 0)
            {
                _logger.LogWarning("[Xlsx] Mistral fail — {N} placeholder dosyası temizleniyor", placeholderPaths.Count);
                foreach (var p in placeholderPaths)
                {
                    try { await _fileStorage.DeleteAsync(p, CancellationToken.None); }
                    catch (Exception delEx) { _logger.LogDebug(delEx, "[Xlsx] Placeholder silme: {Path}", p); }
                }
            }
            throw;
        }

        // PDF için sayfa-bazlı PdfPig fallback (Mistral'in kaçırdığı dijital embedded'ler).
        // Mistral'in görsel hash'leri PdfPig'e verilir; aynı görsel iki kez diske yazılmaz.
        IReadOnlyList<List<string>> pdfEmbeddedPerPage = Array.Empty<List<string>>();
        if (fileType == FileType.Pdf)
        {
            var mistralHashesByPage = mistralPages.ToDictionary(p => p.Index, p => p.ImageHashes);
            pdfEmbeddedPerPage = await ExtractPdfEmbeddedPerPageAsync(bytes, mistralHashesByPage);
        }


        // [Pre] Her sayfa markdown'ında inline image referanslarını [IMG_PATH:...]'a çevir
        for (var i = 0; i < mistralPages.Count; i++)
        {
            var mp = mistralPages[i];
            var md = mp.Markdown;

            for (var local = 0; local < mp.FigurePaths.Count; local++)
                md = md.Replace($"[IMG_REF:{local}]", $"[IMG_PATH:{mp.FigurePaths[local]}]");

            md = EmbedImgPlaceholderRegex.Replace(md, m =>
            {
                var n = int.Parse(m.Groups[1].Value);
                return n >= 0 && n < placeholderPaths.Count
                    ? $"[IMG_PATH:{placeholderPaths[n]}]"
                    : string.Empty;
            });

            mp.Markdown = md;
            mistralPages[i] = mp;
        }

        // ═══════════════════════════════════════════════════════════════════
        //   MODE TOGGLE — config "Chunking:Mode"
        //   "PageBased"  → Mistral OCR ham markdown'unu sayfa-bazlı chunkla (basit, deterministik)
        //   "Semantic"   → Markdig AST + tüm coalescer'lar (mevcut karmaşık akış)
        // ═══════════════════════════════════════════════════════════════════
        if (string.Equals(_chunkingMode, "PageBased", StringComparison.OrdinalIgnoreCase))
        {
            return await ParseAsPageBasedAsync(mistralPages, pdfEmbeddedPerPage, fileType);
        }

        // [1] Her sayfa AST → SemanticBlock listesi
        var tracker = new HeaderChainTracker();
        var allBlocks = new List<SemanticBlock>();
        var globalIdx = 0;
        foreach (var mp in mistralPages.OrderBy(p => p.Index))
        {
            var pageBlocks = _blockExtractor.Extract(mp.Markdown, mp.Index + 1, tracker, ref globalIdx);
            allBlocks.AddRange(pageBlocks);
        }

        // [2a] Sayfa sınırı tablo birleşimi (multi-page tablo)
        allBlocks = _blockMerger.Merge(allBlocks);

        // [2b] Sayfa İÇİ tablo fragman birleşimi — Mistral OCR aynı tabloyu birden fazla
        // parçaya bölüyorsa (aynı header tekrarı veya auto-header continuation) mantıksal
        // bütüne dönüştür. Sonuç: chunker bir mantıksal tabloyu TEK atomic chunk yapar.
        var beforeTableCoalesce = allBlocks.Count;
        allBlocks = _tableCoalescer.Coalesce(allBlocks);
        if (beforeTableCoalesce != allBlocks.Count)
            _logger.LogInformation("[TableCoalesce] {Before} → {After} block ({Merged} tablo fragmanı birleşti)",
                beforeTableCoalesce, allBlocks.Count, beforeTableCoalesce - allBlocks.Count);

        // [2c] Mini-block coalesce — aynı header + aynı tip + tek başına < 80 token olan
        // ardışık Paragraph/List blokları birleşir. Eski MergeSmallAdjacentChunks'ın
        // doğru-katmandaki muadili (rendered markdown'da değil, SemanticBlock seviyesinde).
        var beforeBlockCoalesce = allBlocks.Count;
        allBlocks = _blockCoalescer.Coalesce(allBlocks);
        if (beforeBlockCoalesce != allBlocks.Count)
            _logger.LogInformation("[BlockCoalesce] {Before} → {After} block ({Merged} mini-block birleşti)",
                beforeBlockCoalesce, allBlocks.Count, beforeBlockCoalesce - allBlocks.Count);

        // [3] PdfPig fallback resimleri Y-koordinatla ilgili block'a iliştir
        if (fileType == FileType.Pdf)
        {
            var fallbackImages = new List<ImageWithBbox>();
            for (var pageIdx = 0; pageIdx < pdfEmbeddedPerPage.Count; pageIdx++)
            {
                var pagePaths = pdfEmbeddedPerPage[pageIdx];
                for (var k = 0; k < pagePaths.Count; k++)
                {
                    var normY = pagePaths.Count == 1 ? 0.5 : (k + 0.5) / pagePaths.Count;
                    fallbackImages.Add(new ImageWithBbox(
                        pagePaths[k], pageIdx + 1, normY, 0.5, "PdfPig"));
                }
            }
            if (fallbackImages.Count > 0)
                _imageLinker.Link(allBlocks, fallbackImages);
            // Marker yerleştirme: MarkdownRenderer block.Images'ı tablo altına / paragraf sonuna append eder.
        }

        // [4] SemanticChunker — token bütçesi + tablo atomik + embedding-based semantic split + [IMG_PATH]→[IMG:N]
        // Adaptive: belgenin median paragraf uzunluğuna göre dinamik maxTokens (fallback: config _maxTokens)
        var maxTokensForChunking = _adaptiveEnabled
            ? ComputeAdaptiveMaxTokens(allBlocks)
            : _maxTokens;

        var builtChunks = await _chunker.ChunkAsync(allBlocks, maxTokensForChunking);

        // [5] Duplicate dedup — Mistral OCR'ın bazen tekrar ettiği içerik (SHA256 exact match)
        builtChunks = DeduplicateChunks(builtChunks);

        // NOT: Mini-chunk merge (eski MergeSmallAdjacentChunks) KALDIRILDI.
        // Sebep: rendered markdown'da string concat → [IMG:N] numaraları tekrar ediyor,
        // farklı path'lere işaret ediyor, caption mapping bozuluyor, atomic tablolar parçalanıyor.
        // Doğru katmandaki muadili: BlockCoalescer (SemanticBlock seviyesinde, AST temiz).
        // NOT 2: < 30 char final filter KALDIRILDI — belgeden hiçbir bilgi atılmasın.

        // [6] ParsedChunk dönüşümü
        var final = new List<ParsedChunk>(builtChunks.Count);
        foreach (var c in builtChunks)
        {
            var imagePathJson = c.ImagePaths.Count > 0
                ? JsonSerializer.Serialize(c.ImagePaths)
                : null;

            final.Add(new ParsedChunk(
                Content: c.MarkdownContent,
                ImagePath: imagePathJson,
                Header: string.IsNullOrEmpty(c.Header) ? null : c.Header,
                CleanContent: c.CleanContent,
                PageNumber: c.PageNumber > 0 ? c.PageNumber : null));
        }

        var totalImages = final.Sum(c => CountImagesInJson(c.ImagePath));
        _logger.LogInformation("[Parser] {FileType}: {Chunks} chunk, {Imgs} resim",
            fileType, final.Count, totalImages);
        return final;
    }

    /// <summary>
    /// PageBased mode parser — Markdig AST + HierarchicalChunker + Microsoft TextChunker.
    ///
    /// PIPELINE (sıfır regex, sıfır karakter eşiği, sıfır manuel kural):
    ///   1. Mistral OCR markdown → MarkdownBlockExtractor (Markdig AST walker)
    ///   2. HeaderChainTracker stack → her block kendi H1>H2>H3 hierarşisini taşır
    ///   3. BlockMerger → multi-page tablo birleşimi
    ///   4. TableCoalescer → sayfa içi tablo fragmanları
    ///   5. BlockCoalescer → mini-paragraf birleşimi
    ///   6. ImageLinker → PdfPig fallback görselleri Y-koordinat ile block'lara
    ///   7. HierarchicalChunker → atomic preservation + token-bütçeli split
    ///   8. SHA256 dedup
    /// </summary>
    private async Task<IEnumerable<ParsedChunk>> ParseAsPageBasedAsync(
        List<MistralPage> mistralPages,
        IReadOnlyList<List<string>> pdfEmbeddedPerPage,
        FileType fileType)
    {
        await Task.Yield();

        // [1] Markdig AST → SemanticBlock listesi (sayfa-bazlı, header chain tracker ile)
        var tracker = new HeaderChainTracker();
        var allBlocks = new List<SemanticBlock>();
        var globalIdx = 0;
        foreach (var mp in mistralPages.OrderBy(p => p.Index))
        {
            var pageBlocks = _blockExtractor.Extract(mp.Markdown, mp.Index + 1, tracker, ref globalIdx);
            allBlocks.AddRange(pageBlocks);
        }

        // [2] Sayfa sınırı tablo birleşimi (multi-page)
        allBlocks = _blockMerger.Merge(allBlocks);

        // [3] Sayfa içi tablo fragmanları → tek mantıksal tablo
        var beforeTable = allBlocks.Count;
        allBlocks = _tableCoalescer.Coalesce(allBlocks);
        if (beforeTable != allBlocks.Count)
            _logger.LogInformation("[TableCoalesce] {Before} → {After} block", beforeTable, allBlocks.Count);

        // [4] Mini-block coalesce (ardışık < 80 token paragraflar, AYNI HeaderChain)
        var beforeBlock = allBlocks.Count;
        allBlocks = _blockCoalescer.Coalesce(allBlocks);
        if (beforeBlock != allBlocks.Count)
            _logger.LogInformation("[BlockCoalesce] {Before} → {After} block", beforeBlock, allBlocks.Count);

        // [5] PdfPig fallback görselleri Y-koordinat ile block'lara iliştir
        if (fileType == FileType.Pdf)
        {
            var fallbackImages = new List<ImageWithBbox>();
            for (var pageIdx = 0; pageIdx < pdfEmbeddedPerPage.Count; pageIdx++)
            {
                var pagePaths = pdfEmbeddedPerPage[pageIdx];
                for (var k = 0; k < pagePaths.Count; k++)
                {
                    var normY = pagePaths.Count == 1 ? 0.5 : (k + 0.5) / pagePaths.Count;
                    fallbackImages.Add(new ImageWithBbox(
                        pagePaths[k], pageIdx + 1, normY, 0.5, "PdfPig"));
                }
            }
            if (fallbackImages.Count > 0)
                _imageLinker.Link(allBlocks, fallbackImages);
        }

        // [6] HierarchicalChunker → atomic-aware split
        //   - Table/Code → kendi chunk'ı (atomic)
        //   - Text birikimi → token bütçesinde
        //   - Tek block > maxTokens → TextChunker line-aware fallback
        //   - HeaderChain her chunk'a prepend
        var chunks = _hierarchicalChunker.Chunk(allBlocks);

        // [7] SHA256 dedup (Mistral OCR'ın tekrar verdiği sayfa içerikleri)
        var beforeDedup = chunks.Count;
        chunks = DeduplicateParsedChunks(chunks);

        var totalImages = chunks.Sum(c => CountImagesInJson(c.ImagePath));
        _logger.LogInformation(
            "[Parser:PageBased] {FileType}: {Pages} sayfa → {Blocks} block → {Chunks} chunk{DedupNote}, {Imgs} resim",
            fileType, mistralPages.Count, allBlocks.Count, chunks.Count,
            beforeDedup != chunks.Count ? $" ({beforeDedup - chunks.Count} duplicate atıldı)" : "",
            totalImages);

        return chunks;
    }

    /// <summary>
    /// ParsedChunk listesi için SHA256 hash-based dedup.
    /// Aynı CleanContent'e sahip chunks tekilleşir. < 50 char chunks dedup'tan muaf
    /// (tesadüfi hash çakışmasına karşı koruma).
    /// </summary>
    private List<ParsedChunk> DeduplicateParsedChunks(List<ParsedChunk> chunks)
    {
        if (chunks.Count <= 1) return chunks;

        var seenHashes = new Dictionary<string, int>();
        var result = new List<ParsedChunk>(chunks.Count);

        for (var i = 0; i < chunks.Count; i++)
        {
            var c = chunks[i];
            var content = c.CleanContent ?? c.Content ?? string.Empty;
            if (content.Length < 50)
            {
                result.Add(c);
                continue;
            }
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)));
            if (seenHashes.TryGetValue(hash, out var originalIdx))
            {
                var preview = content.Length > 120 ? content[..120] + "…" : content;
                _logger.LogInformation(
                    "[Dedup:PageBased] Chunk #{Index} atlandı — chunk #{Original} ile aynı. Hash={Hash} | Preview: \"{Preview}\"",
                    i, originalIdx, hash[..8], preview);
                continue;
            }
            seenHashes[hash] = i;
            result.Add(c);
        }

        return result;
    }

    // Aynı SHA256 hash'li chunk'ları filtreler — Mistral OCR'ın bazı sayfalarda tekrar verdiği
    // içerik (örn. aynı tablonun yeniden basımı) DB'ye 2 kez yazılmaz.
    // Universal: sadece hash karşılaştırma, hiçbir dil-spesifik kural yok.
    // Her dedup için INFO log + content preview → API loglarından doğrulayabilirsin
    private List<PipelineChunk> DeduplicateChunks(List<PipelineChunk> chunks)
    {
        if (chunks.Count <= 1) return chunks;

        // hash → ilk chunk index (debug için)
        var seenHashes = new Dictionary<string, int>();
        var result = new List<PipelineChunk>(chunks.Count);
        var skipped = 0;

        for (var i = 0; i < chunks.Count; i++)
        {
            var c = chunks[i];
            var content = c.CleanContent ?? c.MarkdownContent ?? string.Empty;
            // Çok kısa chunk'lar dedup'tan muaf (tesadüfi hash çakışmasına karşı)
            if (content.Length < 50)
            {
                result.Add(c);
                continue;
            }
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)));
            if (seenHashes.TryGetValue(hash, out var originalIdx))
            {
                skipped++;
                // Atılan chunk için detaylı log → user logdan doğrulayabilir
                var preview = content.Length > 120 ? content[..120] + "…" : content;
                _logger.LogInformation(
                    "[Dedup] Chunk #{Index} atlandı — chunk #{Original} ile aynı içerik. Hash={Hash} | Preview: \"{Preview}\"",
                    i, originalIdx, hash[..8], preview);
                continue;
            }
            seenHashes[hash] = i;
            result.Add(c);
        }

        if (skipped > 0)
            _logger.LogInformation("[Dedup] Toplam {N} yinelenen chunk filtrelendi ({Total} chunk → {Kept})",
                skipped, chunks.Count, result.Count);
        else
            _logger.LogInformation("[Dedup] Yinelenen chunk yok ({Total} chunk tutuldu)", chunks.Count);

        return result;
    }

    // Belgenin paragraf token dağılımına göre dinamik maxTokens üretir.
    // Median paragraf × multiplier → clamp(min, max). Çok az/yok paragraf varsa _maxTokens fallback.
    // Sebep: sözleşme belgeleri (kısa madde) için 400, teknik kılavuz için 1500 tipik ihtiyaç.
    private int ComputeAdaptiveMaxTokens(IReadOnlyList<SemanticBlock> blocks)
    {
        var paragraphTokens = blocks
            .Where(b => b.Type == BlockType.Paragraph)
            .Select(b => _tokens.Count(_renderer.ToCleanText(b)))
            .Where(t => t > 5)  // <5 token: muhtemelen footer artığı / image-only paragraph
            .OrderBy(t => t)
            .ToList();

        if (paragraphTokens.Count < 3)
        {
            _logger.LogInformation(
                "[Chunker] Adaptive atlandı (paragraf sayısı {Count} < 3) → fallback maxTokens={Fallback}",
                paragraphTokens.Count, _maxTokens);
            return _maxTokens;
        }

        var median = paragraphTokens[paragraphTokens.Count / 2];
        var adaptive = (int)(median * _adaptiveMultiplier);
        var clamped = Math.Clamp(adaptive, _adaptiveMin, _adaptiveMax);

        _logger.LogInformation(
            "[Chunker] Adaptive maxTokens: median={Median} × {Mult} = {Adaptive} → clamp[{Min}-{Max}] = {Final} ({Paragraphs} paragraf)",
            median, _adaptiveMultiplier, adaptive, _adaptiveMin, _adaptiveMax, clamped, paragraphTokens.Count);

        return clamped;
    }

    private int CountImagesInJson(string? imagePathJson)
    {
        if (string.IsNullOrEmpty(imagePathJson)) return 0;
        try
        {
            using var doc = JsonDocument.Parse(imagePathJson);
            return doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement.GetArrayLength() : 0;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[Parser] ImagePath JSON sayım hatası — 0 dönülüyor");
            return 0;
        }
    }

    private async Task<byte[]> ConvertToPdfAsync(byte[] sourceBytes, string sourceExt, CancellationToken ct)
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), $"doc2pdf_{Guid.NewGuid()}");
        Directory.CreateDirectory(tmpDir);
        var inFile = Path.Combine(tmpDir, $"in.{sourceExt}");
        var outFile = Path.Combine(tmpDir, "in.pdf");

        try
        {
            await File.WriteAllBytesAsync(inFile, sourceBytes, ct);

            // Her çağrıya ayrı UserInstallation profili ver — paralel çağrılar çakışmasın
            var userProfile = new Uri(Path.Combine(tmpDir, "lo_profile")).AbsoluteUri;

            var psi = new ProcessStartInfo
            {
                FileName = _sofficePath,
                Arguments = $"-env:UserInstallation={userProfile} --headless --convert-to pdf --outdir \"{tmpDir}\" \"{inFile}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi)
                ?? throw new InvalidOperationException("LibreOffice (soffice) başlatılamadı. Kurulu mu?");

            // Timeout + outer CT'yi linke et. Timeout 120s; outer CT geldiğinde de iptal.
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(120));

            // stdout/stderr async oku — ThreadPool thread'i bloklanmasın
            var stdoutTask = proc.StandardOutput.ReadToEndAsync(timeoutCts.Token);
            var stderrTask = proc.StandardError.ReadToEndAsync(timeoutCts.Token);

            try
            {
                await proc.WaitForExitAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
            {
                try { proc.Kill(entireProcessTree: true); } catch { /* zaten exit etti olabilir */ }
                throw new TimeoutException("LibreOffice 120 sn içinde tamamlanmadı.");
            }
            catch (OperationCanceledException)
            {
                try { proc.Kill(entireProcessTree: true); } catch { }
                throw;
            }

            var stdout = await stdoutTask;
            var stderr = await stderrTask;

            if (proc.ExitCode != 0)
                throw new InvalidOperationException(
                    $"LibreOffice exit {proc.ExitCode}: {stderr}");

            if (!File.Exists(outFile))
                throw new FileNotFoundException(
                    $"LibreOffice çıktı dosyası yok: {outFile}\nstdout: {stdout}\nstderr: {stderr}");

            var pdfBytes = await File.ReadAllBytesAsync(outFile, ct);
            _logger.LogInformation("[LibreOffice] {Ext} → PDF: {InSize} → {OutSize} byte",
                sourceExt, sourceBytes.Length, pdfBytes.Length);
            return pdfBytes;
        }
        finally
        {
            try { Directory.Delete(tmpDir, recursive: true); } catch { }
        }
    }

    // ImageHashes: Mistral'in bu sayfada yakaladığı görsellerin SHA256 hash'leri.
    // PdfPig fallback'i aynı hash'li görselleri atlar, böylece disk israfı olmaz.
    private class MistralPage
    {
        public int Index { get; set; }
        public string Markdown { get; set; }
        public List<string> FigurePaths { get; set; }
        public HashSet<string> ImageHashes { get; set; }

        public MistralPage(int Index, string Markdown, List<string> FigurePaths, HashSet<string> ImageHashes)
        {
            this.Index = Index;
            this.Markdown = Markdown;
            this.FigurePaths = FigurePaths;
            this.ImageHashes = ImageHashes;
        }
    }

    // 20MB üstü belgeleri /v1/files endpoint'ine multipart upload eder; base64 inline yerine
    // signed URL kullanılır → request body ~67MB string allocation kaybolur. Küçük dosyalarda
    // base64 inline kalır (round-trip yok, bilinen stabil yol).
    private const long MistralLargeFileThreshold = 20L * 1024 * 1024;
    // Worst-case retry zinciri: 1+2+4+8 sleep + 4 × HttpClient.Timeout (120s) → ~8 dakika.
    // Outer global cap: 5 dakika. Çok büyük belgelerde tek istek 120s+ sürebilir; 5dk
    // pratikte 2-3 deneme + retry sleep'ine yeter.
    private static readonly TimeSpan MistralCallGlobalTimeout = TimeSpan.FromMinutes(5);

    private async Task<List<MistralPage>> CallMistralAsync(byte[] bytes, string mime, CancellationToken outerCt)
    {
        // Outer CT + global timeout linked → retry zinciri toplamda 5dk'yı aşmaz.
        using var globalCts = CancellationTokenSource.CreateLinkedTokenSource(outerCt);
        globalCts.CancelAfter(MistralCallGlobalTimeout);
        var ct = globalCts.Token;

        var http = _httpFactory.CreateClient("Mistral");

        string documentUrl;
        if (bytes.Length >= MistralLargeFileThreshold)
        {
            documentUrl = await UploadMistralFileAsync(http, bytes, mime, ct);
            _logger.LogInformation("[Mistral] Büyük dosya ({MB} MB) /v1/files endpoint ile gönderildi (base64 bypass)",
                bytes.Length / (1024 * 1024));
        }
        else
        {
            documentUrl = $"data:{mime};base64,{Convert.ToBase64String(bytes)}";
        }

        var payload = new
        {
            model = _mistralModel,
            document = new { type = "document_url", document_url = documentUrl },
            include_image_base64 = true
        };

        JsonDocument? json = null;
        for (var attempt = 1; attempt <= 4; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using var resp = await http.PostAsJsonAsync("/v1/ocr", payload, ct);
                if (!resp.IsSuccessStatusCode)
                {
                    var body = await resp.Content.ReadAsStringAsync(ct);
                    _logger.LogWarning("[Mistral] HTTP {S} try {N}/4: {B}",
                        (int)resp.StatusCode, attempt, body.Length > 400 ? body[..400] : body);
                    if (attempt < 4 && (int)resp.StatusCode >= 500)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt - 1)), ct);
                        continue;
                    }
                    resp.EnsureSuccessStatusCode();
                }
                // Stream-based parse: response string'e materialize edilmiyor (~50MB tasarruf büyük OCR'larda)
                using var respStream = await resp.Content.ReadAsStreamAsync(ct);
                json = await JsonDocument.ParseAsync(respStream, cancellationToken: ct);
                break;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (attempt < 4)
            {
                _logger.LogWarning("[Mistral] {T}: {M} retry {N}/4", ex.GetType().Name, ex.Message, attempt);
                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt - 1)), ct);
            }
        }
        if (json is null) throw new InvalidOperationException("Mistral OCR yanıt vermedi.");

        var pages = new List<MistralPage>();
        if (!json.RootElement.TryGetProperty("pages", out var pagesEl) || pagesEl.ValueKind != JsonValueKind.Array)
            return pages;

        // Global hash → path map. Mistral aynı resmi (logo, header) HER sayfada base64 olarak
        // dönebilir. Burada hash bazında dedup → disk'e tek kopya yazılır, sayfalar aynı path'i
        // paylaşır → disk + DB israfı önlenir.
        var globalHashToPath = new Dictionary<string, string>(StringComparer.Ordinal);
        var dedupedCount = 0;

        foreach (var pageEl in pagesEl.EnumerateArray())
        {
            var idx = pageEl.TryGetProperty("index", out var ie) && ie.TryGetInt32(out var i) ? i : pages.Count;
            var md = pageEl.TryGetProperty("markdown", out var me) ? (me.GetString() ?? "") : "";
            var figs = new List<string>();
            var hashes = new HashSet<string>(StringComparer.Ordinal);

            if (pageEl.TryGetProperty("images", out var imgsEl) && imgsEl.ValueKind == JsonValueKind.Array)
            {
                var localIdx = 0;
                foreach (var imgEl in imgsEl.EnumerateArray())
                {
                    var imgId = imgEl.TryGetProperty("id", out var ide) ? ide.GetString() : null;
                    var b64 = imgEl.TryGetProperty("image_base64", out var be) ? be.GetString() : null;
                    if (string.IsNullOrWhiteSpace(b64)) continue;
                    var c = b64.IndexOf(',');
                    if (c >= 0) b64 = b64[(c + 1)..];
                    byte[] imgBytes;
                    try { imgBytes = Convert.FromBase64String(b64); }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "[Parser] Base64 decode atlandı — image id={ImgId}", imgId);
                        continue;
                    }
                    if (imgBytes.Length < 64) continue;

                    // Hash, PdfPig fallback ile dedup için hesaplanır
                    var hash = Convert.ToHexString(SHA256.HashData(imgBytes));
                    hashes.Add(hash);

                    string path;
                    if (globalHashToPath.TryGetValue(hash, out var existingPath))
                    {
                        // Aynı hash daha önce görüldü → diske TEKRAR yazma, mevcut path'i kullan
                        path = existingPath;
                        dedupedCount++;
                    }
                    else
                    {
                        var ext = ImageMagicBytes.DetectExtension(imgBytes);
                        using var ms = new MemoryStream(imgBytes);
                        path = await _fileStorage.SaveRawAsync(ms, $"img_{Guid.NewGuid()}.{ext}");
                        globalHashToPath[hash] = path;
                    }
                    figs.Add(path);

                    if (!string.IsNullOrEmpty(imgId))
                    {
                        var pattern = @"!\[[^\]]*\]\(" + Regex.Escape(imgId) + @"\)";
                        md = Regex.Replace(md, pattern, $"[IMG_REF:{localIdx}]");
                    }
                    localIdx++;
                }
            }
            pages.Add(new MistralPage(idx, md, figs, hashes));
        }
        _logger.LogInformation(
            "[Mistral] {P} sayfa, {F} figure (unique: {U}{DedupNote})",
            pages.Count, pages.Sum(p => p.FigurePaths.Count), globalHashToPath.Count,
            dedupedCount > 0 ? $", {dedupedCount} tekrar dedup edildi" : "");
        return pages;
    }

    // Büyük dosyayı Mistral /v1/files endpoint'ine multipart yükler, signed URL döner.
    // Base64 inline (~33% boyut artışı + ek string allocation) yerine doğrudan binary upload.
    private async Task<string> UploadMistralFileAsync(HttpClient http, byte[] bytes, string mime, CancellationToken ct)
    {
        using var form = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(mime);
        form.Add(fileContent, "file", $"upload.{ExtensionFor(mime)}");
        form.Add(new StringContent("ocr"), "purpose");

        using var uploadResp = await http.PostAsync("/v1/files", form, ct);
        if (!uploadResp.IsSuccessStatusCode)
        {
            var body = await uploadResp.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException(
                $"Mistral /v1/files upload başarısız ({(int)uploadResp.StatusCode}): {(body.Length > 400 ? body[..400] : body)}");
        }

        string fileId;
        using (var uploadStream = await uploadResp.Content.ReadAsStreamAsync(ct))
        using (var uploadDoc = await JsonDocument.ParseAsync(uploadStream, cancellationToken: ct))
        {
            fileId = uploadDoc.RootElement.TryGetProperty("id", out var idEl) ? (idEl.GetString() ?? "") : "";
            if (string.IsNullOrEmpty(fileId))
                throw new InvalidOperationException("Mistral /v1/files response'unda 'id' alanı yok.");
        }

        using var urlResp = await http.GetAsync($"/v1/files/{fileId}/url?expiry=24", ct);
        if (!urlResp.IsSuccessStatusCode)
        {
            var body = await urlResp.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException(
                $"Mistral signed URL alınamadı ({(int)urlResp.StatusCode}): {(body.Length > 400 ? body[..400] : body)}");
        }

        using var urlStream = await urlResp.Content.ReadAsStreamAsync(ct);
        using var urlDoc = await JsonDocument.ParseAsync(urlStream, cancellationToken: ct);
        var signed = urlDoc.RootElement.TryGetProperty("url", out var urlEl) ? urlEl.GetString() : null;
        if (string.IsNullOrEmpty(signed))
            throw new InvalidOperationException("Mistral signed URL response'unda 'url' alanı yok.");
        return signed;
    }

    private static string ExtensionFor(string mime) => mime switch
    {
        "application/pdf"                                                                => "pdf",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document"        => "docx",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"              => "xlsx",
        "text/csv"                                                                       => "csv",
        "application/msword"                                                             => "doc",
        _                                                                                => "bin"
    };

    private async Task<List<List<string>>> ExtractPdfEmbeddedPerPageAsync(
        byte[] pdfBytes,
        IReadOnlyDictionary<int, HashSet<string>>? mistralHashesByPage = null)
    {
        var result = new List<List<string>>();
        var skippedDuplicates = 0;
        try
        {
            using var doc = PdfDocument.Open(pdfBytes);
            for (var pageNum = 1; pageNum <= doc.NumberOfPages; pageNum++)
            {
                var page = doc.GetPage(pageNum);
                var imgs = page.GetImages()
                    .OrderByDescending(img => img.Bounds.Top)
                    .ThenBy(img => img.Bounds.Left)
                    .ToList();
                var pagePaths = new List<string>();

                // Mistral'in bu sayfada yakaladığı hash'ler — duplicate atlamak için
                mistralHashesByPage?.TryGetValue(pageNum - 1, out var mistralHashes);
                var mistralHashesSet = mistralHashesByPage != null
                    && mistralHashesByPage.TryGetValue(pageNum - 1, out var h) ? h : null;

                foreach (var img in imgs)
                {
                    byte[]? imgBytes = null;
                    if (img.TryGetPng(out var png)) imgBytes = png;
                    else if (img.TryGetBytesAsMemory(out var mem)) imgBytes = mem.ToArray();
                    else if (img.RawMemory.Length > 0) imgBytes = img.RawMemory.ToArray();
                    if (imgBytes is null || imgBytes.Length < 64) continue;

                    // Mistral aynı görseli zaten yakaladıysa diske yazma
                    var hash = Convert.ToHexString(SHA256.HashData(imgBytes));
                    if (mistralHashesSet != null && mistralHashesSet.Contains(hash))
                    {
                        skippedDuplicates++;
                        continue;
                    }

                    var ext = ImageMagicBytes.DetectExtension(imgBytes);
                    using var ms = new MemoryStream(imgBytes);
                    var path = await _fileStorage.SaveRawAsync(ms, $"img_{Guid.NewGuid()}.{ext}");
                    pagePaths.Add(path);
                }
                result.Add(pagePaths);
            }
            _logger.LogInformation(
                "[PdfPig] {C} embedded resim, {P} sayfa{DupNote}",
                result.Sum(p => p.Count), result.Count,
                skippedDuplicates > 0 ? $" ({skippedDuplicates} duplicate Mistral ile çakıştığı için skip edildi)" : "");
        }
        catch (Exception ex) { _logger.LogWarning("[PdfPig] Hata: {M}", ex.Message); }
        return result;
    }

    private class PreparedDoc
    {
        public byte[] Bytes { get; set; }
        public List<string> Paths { get; set; }

        public PreparedDoc(byte[] Bytes, List<string> Paths)
        {
            this.Bytes = Bytes;
            this.Paths = Paths;
        }
    }


    private async Task<PreparedDoc> PrepareXlsxWithPlaceholdersAsync(byte[] originalBytes)
    {
        var paths = new List<string>();
        try
        {
            using var inMs = new MemoryStream(originalBytes);
            using var workbook = new XLWorkbook(inMs);
            foreach (var ws in workbook.Worksheets)
            {
                var pictures = ws.Pictures
                    .OrderBy(p => p.TopLeftCell?.Address.RowNumber ?? 0)
                    .ThenBy(p => p.TopLeftCell?.Address.ColumnNumber ?? 0)
                    .ToList();
                foreach (var pic in pictures)
                {
                    using var imgMs = new MemoryStream();
                    pic.ImageStream.CopyTo(imgMs);
                    var imgBytes = imgMs.ToArray();
                    if (imgBytes.Length < 64) continue;

                    var ext = ImageMagicBytes.DetectExtension(imgBytes);
                    using var save = new MemoryStream(imgBytes);
                    var path = await _fileStorage.SaveRawAsync(save, $"img_{Guid.NewGuid()}.{ext}");
                    paths.Add(path);

                    var token = $"[[EMBED_IMG_{paths.Count - 1}]]";
                    var cell = pic.TopLeftCell;
                    if (cell is not null)
                    {
                        var existing = cell.GetString()?.Trim();
                        cell.Value = string.IsNullOrWhiteSpace(existing) ? token : $"{existing} {token}";
                    }
                    pic.Delete();
                }
            }
            using var outMs = new MemoryStream();
            workbook.SaveAs(outMs);
            _logger.LogInformation("[Xlsx] {C} embedded resim placeholder ile işaretlendi", paths.Count);
            return new PreparedDoc(outMs.ToArray(), paths);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("[Xlsx] Placeholder hatası: {M}", ex.Message);
            return new PreparedDoc(originalBytes, paths);
        }
    }


    private async Task<byte[]> ExtractHtmlFromMhtmlAsync(byte[] mhtmlBytes)
    {
        try
        {
            using var ms = new MemoryStream(mhtmlBytes);
            var msg = MimeKit.MimeMessage.Load(ms);

            string? html = null;
            var cidToDataUri = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var allImageDataUris = new List<string>(); // HTML'in referans etmediği "sahipsiz" resimleri buradan ekleyeceğiz

            foreach (var part in msg.BodyParts)
            {
                if (part is not MimeKit.MimePart mime) continue;
                if (mime.ContentType.MimeType == "text/html")
                {
                    using var sr = new StreamReader(mime.Content.Open());
                    html = sr.ReadToEnd();
                }
                else if (mime.ContentType.MediaType == "image")
                {
                    using var imgMs = new MemoryStream();
                    mime.Content.DecodeTo(imgMs);
                    var b64 = Convert.ToBase64String(imgMs.ToArray());
                    var dataUri = $"data:{mime.ContentType.MimeType};base64,{b64}";
                    allImageDataUris.Add(dataUri);

                    var cid = mime.ContentId?.Trim('<', '>');
                    if (!string.IsNullOrEmpty(cid)) cidToDataUri[cid] = dataUri;
                    var loc = mime.ContentLocation?.ToString();
                    if (!string.IsNullOrEmpty(loc)) cidToDataUri[loc] = dataUri;
                    if (!string.IsNullOrEmpty(mime.FileName)) cidToDataUri[mime.FileName] = dataUri;
                }
            }

            if (string.IsNullOrWhiteSpace(html))
            {
                _logger.LogWarning("[MHTML] HTML body bulunamadı, raw bytes geri döndürülüyor");
                return mhtmlBytes;
            }

            // 1) cid: ve içerik-konum referanslarını data URI ile değiştir
            foreach (var (key, dataUri) in cidToDataUri)
            {
                html = html.Replace($"cid:{key}", dataUri, StringComparison.OrdinalIgnoreCase);
                html = html.Replace($"\"{key}\"", $"\"{dataUri}\"", StringComparison.OrdinalIgnoreCase);
                html = html.Replace($"'{key}'", $"'{dataUri}'", StringComparison.OrdinalIgnoreCase);
            }

            // 2) HTML'i parse et: dış URL'li <img> tag'lerini fetch edip data URI'ye çevir
            var doc = new HtmlAgilityPack.HtmlDocument();
            doc.LoadHtml(html);
            var imgNodes = doc.DocumentNode.SelectNodes("//img");
            var referencedDataUris = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int fetched = 0, alreadyData = 0;
            if (imgNodes != null)
            {
                // IHttpClientFactory'den havuzlu client — DNS güncellemeleri ve socket exhaustion'a güvenli.
                var http = _httpFactory.CreateClient();
                http.Timeout = TimeSpan.FromSeconds(10);
                foreach (var img in imgNodes)
                {
                    var src = img.GetAttributeValue("src", string.Empty);
                    if (string.IsNullOrWhiteSpace(src)) continue;
                    if (src.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                    {
                        referencedDataUris.Add(src);
                        alreadyData++;
                        continue;
                    }
                    if (src.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                        src.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            var bytes = await http.GetByteArrayAsync(src);
                            if (bytes.Length < 64) continue;
                            var mime = ImageMagicBytes.DetectMimeType(bytes);
                            var dataUri = $"data:{mime};base64,{Convert.ToBase64String(bytes)}";
                            img.SetAttributeValue("src", dataUri);
                            referencedDataUris.Add(dataUri);
                            fetched++;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogDebug("[MHTML] Dış URL fetch hatası: {Url} → {Msg}", src, ex.Message);
                        }
                    }
                }
                html = doc.DocumentNode.OuterHtml;
            }

            // 3) MHTML embed'leri arasında HTML hiç referans etmeyenleri (sahipsiz resim) body sonuna ekle
            var unreferencedImages = allImageDataUris.Where(u => !referencedDataUris.Contains(u)
                                                              && !html.Contains(u)).ToList();
            if (unreferencedImages.Count > 0)
            {
                var sb = new StringBuilder();
                sb.AppendLine("<div>");
                foreach (var dataUri in unreferencedImages)
                    sb.AppendLine($"<p><img src=\"{dataUri}\" style=\"max-width:600px\" /></p>");
                sb.AppendLine("</div>");
                var idx = html.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
                html = idx >= 0
                    ? html.Insert(idx, sb.ToString())
                    : html + sb.ToString();
            }

            _logger.LogInformation("[MHTML] {Chars} kar, {Embed} embed, {Fetch} dış URL fetch, {Data} mevcut data URI, {Unref} sahipsiz",
                html.Length, cidToDataUri.Count, fetched, alreadyData, unreferencedImages.Count);
            return Encoding.UTF8.GetBytes(html);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("[MHTML] Parse hatası: {Msg} — raw bytes geri döndürülüyor", ex.Message);
            return mhtmlBytes;
        }
    }

    private static bool IsMhtml(byte[] bytes)
    {
        if (bytes.Length < 20) return false;
        var head = Encoding.ASCII.GetString(bytes, 0, Math.Min(200, bytes.Length));
        return head.StartsWith("Message-ID:", StringComparison.OrdinalIgnoreCase)
            || head.StartsWith("MIME-Version:", StringComparison.OrdinalIgnoreCase)
            || head.StartsWith("From:", StringComparison.OrdinalIgnoreCase)
            || head.Contains("Content-Type: multipart/related", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<byte[]> ReadAllBytesAsync(Stream stream, CancellationToken ct)
    {
        if (stream.CanSeek) stream.Position = 0;
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms, ct);
        if (stream.CanSeek) stream.Position = 0;
        return ms.ToArray();
    }

    private static string MimeFor(FileType ft) => ft switch
    {
        FileType.Pdf  => "application/pdf",
        FileType.Docx => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        FileType.Xlsx => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        FileType.Csv  => "text/csv",
        FileType.Doc  => "application/msword",
        _             => "application/octet-stream"
    };
}
