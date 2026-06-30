using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Pgvector;
using DocuChat.Domain.Entities;

namespace DocuChat.Infrastructure.Persistence.Configurations;

public class DocumentImageConfiguration : IEntityTypeConfiguration<DocumentImage>
{
    public void Configure(EntityTypeBuilder<DocumentImage> b)
    {
        b.HasKey(x => x.Id);

        b.Property(x => x.Path).IsRequired().HasMaxLength(1024);
        b.Property(x => x.ContentHash).HasMaxLength(64);

        // CLIP görsel embedding — nullable vector(512). Null kayıtlar HNSW index'e girmez.
        var visualConverter = new ValueConverter<float[]?, Vector?>(
            v => v != null ? new Vector(v) : null,
            v => v != null ? v.ToArray() : null);
        b.Property(x => x.VisualEmbedding)
         .HasColumnType("vector(512)")
         .HasConversion(visualConverter);

        b.HasIndex(x => x.DocumentId);
        b.HasIndex(x => x.ContentHash);
        b.HasIndex(x => x.VisualEmbedding)
         .HasMethod("hnsw")
         .HasOperators("vector_cosine_ops");

        b.HasOne(x => x.Document)
         .WithMany(d => d.Images)
         .HasForeignKey(x => x.DocumentId)
         .OnDelete(DeleteBehavior.Cascade);
    }
}
