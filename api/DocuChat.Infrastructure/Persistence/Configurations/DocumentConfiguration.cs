using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using DocuChat.Domain.Entities;
using DocuChat.Infrastructure.Persistence.Identity;

namespace DocuChat.Infrastructure.Persistence.Configurations;

public class DocumentConfiguration : IEntityTypeConfiguration<Document>
{
    public void Configure(EntityTypeBuilder<Document> builder)
    {
        builder.HasKey(d => d.Id);

        builder.Property(d => d.FileName)
               .IsRequired()
               .HasMaxLength(255);

        builder.Property(d => d.ContentType)
               .IsRequired()
               .HasMaxLength(100);

        builder.Property(d => d.UserId).IsRequired();

        builder.HasOne<AppUser>()
               .WithMany()
               .HasForeignKey(d => d.UserId)
               .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(d => d.UserId);
        builder.HasIndex(d => d.Status);
        builder.HasIndex(d => d.CreatedAt);

        // Aynı kullanıcı + aynı dosya adı eşzamanlı upload race koruması.
        // App-level ExistsByUserAndNameAsync check ile birlikte ikinci savunma katmanı.
        builder.HasIndex(d => new { d.UserId, d.FileName }).IsUnique();

        // Content hash dedup — aynı içerik farklı isimlerle yeniden yüklendiğinde tespit.
        // SHA256 hex = sabit 64 char. UNIQUE değil (farklı kullanıcılar aynı belgeyi yüklüyor olabilir).
        builder.Property(d => d.ContentHash).HasMaxLength(64);
        builder.HasIndex(d => new { d.UserId, d.ContentHash });
    }
}
