using DocuChat.Infrastructure.Services.Documents.Parsing.Models;

namespace DocuChat.Infrastructure.Services.Documents.Parsing.Linking;

public sealed class ImageLinker
{
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
            var bestBlock = pageBlocks[0];
            var bestDist = double.MaxValue;

            for (var i = 0; i < pageBlocks.Count; i++)
            {
                var blockNormY = (i + 0.5) / pageBlocks.Count;
                var dist = Math.Abs(blockNormY - image.NormY);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    bestBlock = pageBlocks[i];
                }
            }

            bestBlock.Images.Add(image);
        }
    }
}
