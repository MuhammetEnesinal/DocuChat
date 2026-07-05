using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Pgvector;
using DocuChat.Domain.Entities;
using DocuChat.Domain.Entities.Common;
using DocuChat.Domain.Entities.Chat;
using DocuChat.Domain.Entities.Documents;
using DocuChat.Domain.Entities.Caching;

namespace DocuChat.Infrastructure.Persistence.Configurations.Chat;

public class ChatMessageFeedbackConfiguration : IEntityTypeConfiguration<ChatMessageFeedback>
{
    public void Configure(EntityTypeBuilder<ChatMessageFeedback> b)
    {
        b.HasKey(x => x.Id);

        b.Property(x => x.UserId).IsRequired().HasMaxLength(450);
        b.Property(x => x.QuestionText).IsRequired();
        b.Property(x => x.AnswerText).IsRequired();
        b.Property(x => x.Rating).IsRequired();

        // ReasonCategories: PostgreSQL text[] — sorgu ve agregasyon için
        b.Property(x => x.ReasonCategories).HasColumnType("text[]");

        b.Property(x => x.ReasonText).HasMaxLength(500);

        // BGE-M3 embedding (1024-dim) — soru benzerliği matching için
        var vectorConverter = new ValueConverter<float[], Vector>(
            v => new Vector(v),
            v => v.ToArray());
        var vectorComparer = new ValueComparer<float[]>(
            (a, b) => a != null && b != null && a.SequenceEqual(b),
            c => c.Aggregate(0, (a, v) => HashCode.Combine(a, v.GetHashCode())),
            c => c.ToArray());

        b.Property(x => x.QuestionVector)
         .HasColumnType("vector(1024)")
         .HasConversion(vectorConverter)
         .Metadata.SetValueComparer(vectorComparer);

        // HNSW index — cosine similarity hızlı arama
        b.HasIndex(x => x.QuestionVector)
         .HasMethod("hnsw")
         .HasOperators("vector_cosine_ops");

        // Mesaj silinirse feedback de cascade silinir
        b.HasOne(x => x.Message)
         .WithMany()
         .HasForeignKey(x => x.MessageId)
         .OnDelete(DeleteBehavior.Cascade);

        // Bir kullanıcı bir mesaja sadece 1 feedback verebilir
        b.HasIndex(x => new { x.UserId, x.MessageId }).IsUnique();

        b.HasIndex(x => x.UserId);
        b.HasIndex(x => x.Rating);
        b.HasIndex(x => x.CreatedAt);
    }
}
