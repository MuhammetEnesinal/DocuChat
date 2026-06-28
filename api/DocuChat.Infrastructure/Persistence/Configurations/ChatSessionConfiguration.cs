using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using DocuChat.Domain.Entities;
using DocuChat.Infrastructure.Persistence.Identity;

namespace DocuChat.Infrastructure.Persistence.Configurations;

public class ChatSessionConfiguration : IEntityTypeConfiguration<ChatSession>
{
    public void Configure(EntityTypeBuilder<ChatSession> builder)
    {
        builder.HasKey(s => s.Id);
        builder.Property(s => s.UserId).IsRequired();
        builder.Property(s => s.Title).HasMaxLength(100);

        builder.HasOne<AppUser>()
               .WithMany()
               .HasForeignKey(s => s.UserId)
               .OnDelete(DeleteBehavior.Cascade);

        // Arşivleme & sabitleme — UI sıralaması/filtresi sık kullanır, composite index ile hızlı.
        builder.Property(s => s.IsArchived).HasDefaultValue(false);
        builder.Property(s => s.IsPinned).HasDefaultValue(false);
        builder.HasIndex(s => new { s.UserId, s.IsArchived, s.IsPinned });
    }
}
