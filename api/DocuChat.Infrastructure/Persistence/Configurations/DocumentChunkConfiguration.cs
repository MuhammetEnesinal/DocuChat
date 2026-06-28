using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using NpgsqlTypes;
using Pgvector;
using DocuChat.Domain.Entities;

namespace DocuChat.Infrastructure.Persistence.Configurations;

public class DocumentChunkConfiguration : IEntityTypeConfiguration<DocumentChunk>
{
    public void Configure(EntityTypeBuilder<DocumentChunk> builder)
    {
        builder.HasKey(c => c.Id);

        var converter = new ValueConverter<float[], Vector>(
            v => new Vector(v),
            v => v.ToArray());

        var comparer = new ValueComparer<float[]>(
            (a, b) => a != null && b != null && a.SequenceEqual(b),
            c => c.Aggregate(0, (a, v) => HashCode.Combine(a, v.GetHashCode())),
            c => c.ToArray());

        builder.Property(c => c.Embedding)
        .HasColumnType("vector(1024)")  // 768 → 1024
                .HasConversion(converter)
               .Metadata.SetValueComparer(comparer);

        builder.HasIndex(c => c.DocumentId);

        // TsVector — DB'de GENERATED STORED column. Shadow property: Domain entity'sinde yok,
        builder.Property<NpgsqlTsVector>("TsVector")
               .HasColumnName("TsVector")
               .HasColumnType("tsvector")
               .ValueGeneratedOnAddOrUpdate();
        var tsv = builder.Metadata.FindProperty("TsVector");
        if (tsv != null)
        {
            tsv.SetBeforeSaveBehavior(PropertySaveBehavior.Ignore);
            tsv.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
        }

        builder.HasIndex(c => new { c.DocumentId, c.PageNumber });

        builder.HasIndex(c => c.Embedding)
               .HasMethod("hnsw")
               .HasOperators("vector_cosine_ops");

        builder.HasOne(c => c.Document)
               .WithMany(d => d.Chunks)
               .HasForeignKey(c => c.DocumentId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}
