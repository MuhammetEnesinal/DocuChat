using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
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
        b.Property(x => x.Caption).HasColumnType("text");
        b.Property(x => x.ImageType).HasMaxLength(20);
        b.Property(x => x.Source).HasMaxLength(20);
        b.Property(x => x.ContentHash).HasMaxLength(64);

        // CLIP embedding vector(512) — şimdilik null, ileride doldurulur
        var converter = new ValueConverter<float[]?, Vector?>(
            v => v == null ? null : new Vector(v),
            v => v == null ? null : v.ToArray());
        var comparer = new ValueComparer<float[]?>(
            (a, b) => a == b || (a != null && b != null && a.SequenceEqual(b)),
            c => c == null ? 0 : c.Aggregate(0, (a, v) => HashCode.Combine(a, v.GetHashCode())),
            c => c == null ? null : c.ToArray());

        b.Property(x => x.Embedding)
         .HasColumnType("vector(512)")
         .HasConversion(converter)
         .Metadata.SetValueComparer(comparer);

        b.HasIndex(x => x.DocumentId);
        b.HasIndex(x => x.ContentHash);

        b.HasOne(x => x.Document)
         .WithMany(d => d.Images)
         .HasForeignKey(x => x.DocumentId)
         .OnDelete(DeleteBehavior.Cascade);
    }
}
