using DocuChat.Infrastructure.Services.Documents.Parsing.Models;

namespace DocuChat.Infrastructure.Services.Documents.Parsing.Linking;

public sealed class ImageLinker
{
    // Resimleri (PdfPig fallback) en yakın SemanticBlock'a iliştirir.
    // İki kademeli algoritma:
    // 1. CONTAINMENT MATCH — image.NormY ∈ [block.BboxTopNormY, block.BboxBottomNormY]
    // direkt match (en doğru), score = 0
    // 2. NEAREST FALLBACK — bbox dışındaki resim için en yakın kenara mesafe
    // (bbox varsa kenar mesafesi, yoksa uniform spacing yaklaşımı)
    public void Link(IReadOnlyList<SemanticBlock> blocks, IReadOnlyList<ImageWithBbox> images)
    {
        if (blocks.Count == 0 || images.Count == 0) return;

        var blocksByPage = blocks
            .GroupBy(b => b.PageNumber)
            .ToDictionary(g => g.Key, g => g.OrderBy(b => b.Index).ToList());

        var allPages = blocksByPage.Keys.OrderBy(p => p).ToList();

        foreach (var image in images)
        {
            var targetPage = image.PageNumber;

            // Sayfada hiç block yoksa → en yakın komşu sayfa
            if (!blocksByPage.ContainsKey(targetPage))
            {
                if (allPages.Count == 0) continue;
                targetPage = allPages.OrderBy(p => Math.Abs(p - image.PageNumber)).First();
            }

            var pageBlocks = blocksByPage[targetPage];
            var bestBlock = ResolveBestBlock(pageBlocks, image.NormY);
            bestBlock?.Images.Add(image);
        }
    }

    // İki aşamalı eşleştirme:
    // Pass 1 — bbox containment: image.NormY block aralığında ise direkt seç (score 0).
    // Birden fazla containment durumunda ilki kullanılır.
    // Pass 2 — fallback: bbox kenar mesafesi (bbox varsa) veya uniform spacing mesafesi.
    private static SemanticBlock? ResolveBestBlock(IReadOnlyList<SemanticBlock> pageBlocks, double imageNormY)
    {
        if (pageBlocks.Count == 0) return null;

        // Pass 1: containment
        foreach (var b in pageBlocks)
        {
            if (b.BboxTopNormY is double top && b.BboxBottomNormY is double bot
                && imageNormY >= top && imageNormY <= bot)
            {
                return b;
            }
        }

        // Pass 2: nearest
        SemanticBlock? bestBlock = pageBlocks[0];
        double bestDist = double.MaxValue;
        for (var i = 0; i < pageBlocks.Count; i++)
        {
            var b = pageBlocks[i];
            double dist;
            if (b.BboxTopNormY is double top && b.BboxBottomNormY is double bot)
            {
                // Bbox dışında — en yakın kenara mesafe
                dist = imageNormY < top ? top - imageNormY : imageNormY - bot;
            }
            else
            {
                // Uniform spacing fallback (split'le üretilmiş bbox'sız piece'ler için)
                var blockNormY = (i + 0.5) / pageBlocks.Count;
                dist = Math.Abs(blockNormY - imageNormY);
            }
            if (dist < bestDist) { bestDist = dist; bestBlock = b; }
        }
        return bestBlock;
    }
}
