using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using DocuChat.Domain.Entities;
using DocuChat.Domain.Entities.Common;
using DocuChat.Domain.Entities.Chat;
using DocuChat.Domain.Entities.Documents;
using DocuChat.Domain.Entities.Caching;

namespace DocuChat.Infrastructure.Persistence.Configurations.Documents;

public class ChunkImageConfiguration : IEntityTypeConfiguration<ChunkImage>
{
    public void Configure(EntityTypeBuilder<ChunkImage> b)
    {
        b.HasKey(x => x.Id);

        b.HasIndex(x => x.ChunkId);
        b.HasIndex(x => x.ImageId);
        // Bir chunk içinde aynı görsel sadece bir kez (aynı pozisyonda)
        b.HasIndex(x => new { x.ChunkId, x.ImageId, x.PositionInChunk }).IsUnique();

        b.HasOne(x => x.Chunk)
         .WithMany(c => c.ImageLinks)
         .HasForeignKey(x => x.ChunkId)
         .OnDelete(DeleteBehavior.Cascade);

        b.HasOne(x => x.Image)
         .WithMany(i => i.ChunkLinks)
         .HasForeignKey(x => x.ImageId)
         .OnDelete(DeleteBehavior.Cascade);
    }
}
