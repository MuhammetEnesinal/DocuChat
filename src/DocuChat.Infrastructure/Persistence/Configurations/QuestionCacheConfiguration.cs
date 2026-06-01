using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Pgvector;
using DocuChat.Domain.Entities;

namespace DocuChat.Infrastructure.Persistence.Configurations;

public class QuestionCacheConfiguration : IEntityTypeConfiguration<QuestionCache>
{
    public void Configure(EntityTypeBuilder<QuestionCache> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.QuestionText).IsRequired();
        builder.Property(x => x.Answer).IsRequired();
        builder.Property(x => x.HitCount).HasDefaultValue(0);

        var converter = new ValueConverter<float[], Vector>(
            v => new Vector(v),
            v => v.ToArray());

        var comparer = new ValueComparer<float[]>(
            (a, b) => a != null && b != null && a.SequenceEqual(b),
            c => c.Aggregate(0, (a, v) => HashCode.Combine(a, v.GetHashCode())),
            c => c.ToArray());

        builder.Property(x => x.QuestionVector)
               .HasColumnType("vector(1024)")
               .HasConversion(converter)
               .Metadata.SetValueComparer(comparer);

        builder.HasIndex(x => x.QuestionVector)
               .HasMethod("hnsw")
               .HasOperators("vector_cosine_ops");

        builder.HasIndex(x => x.LastHitAt);
        builder.HasIndex(x => x.CreatedAt);
    }
}
