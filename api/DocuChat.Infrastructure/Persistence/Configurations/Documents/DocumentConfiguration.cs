using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using DocuChat.Domain.Entities;
using DocuChat.Domain.Entities.Common;
using DocuChat.Domain.Entities.Chat;
using DocuChat.Domain.Entities.Documents;
using DocuChat.Domain.Entities.Caching;
using DocuChat.Infrastructure.Persistence.Identity;

namespace DocuChat.Infrastructure.Persistence.Configurations.Documents;

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

        // UserId yalnızca yükleyen kişiyi bilgi amaçlı tutar; belgenin sahipliğini departman
        // belirler. SetNull davranışı seçilir: kullanıcı silindiğinde belge korunur, yalnız
        // yükleyen bilgisi boşalır. Cascade silme burada uygun değildir; bir hesabın silinmesi
        // departmanın belgelerini götürür ve uygulama kodunu atladığı için diskteki dosyalar ile
        // cache kayıtları geride kalırdı.
        builder.HasOne<AppUser>()
               .WithMany()
               .HasForeignKey(d => d.UserId)
               .OnDelete(DeleteBehavior.SetNull);

        // Belge → Departman (zorunlu). Restrict: içinde belge olan departman silinemez
        // (referential integrity — departman silme admin tarafında ayrıca bloklanır).
        builder.Property(d => d.DepartmentId).IsRequired();
        builder.HasOne(d => d.Department)
               .WithMany(dep => dep.Documents)
               .HasForeignKey(d => d.DepartmentId)
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(d => d.UserId);
        builder.HasIndex(d => d.DepartmentId);
        builder.HasIndex(d => d.Status);
        builder.HasIndex(d => d.CreatedAt);

        // Dedup kapsamı DEPARTMAN (kullanıcı değil): aynı departmana aynı dosya iki kez giremez,
        // ama FARKLI departmanlara aynı dosya yüklenebilir (her departmanın kendi kopyası olur).
        // Kullanıcı bazlı olsaydı: aynı kişi aynı dosyayı 2 departmana koyamaz, farklı kişiler ise
        // aynı departmana aynı dosyayı 2 kez koyabilirdi — ikisi de yanlış.
        // App-level check ile birlikte eşzamanlı upload race'ine karşı ikinci savunma katmanı.
        builder.HasIndex(d => new { d.DepartmentId, d.FileName }).IsUnique();

        // Content hash dedup — aynı içerik farklı isimle yeniden yüklenirse tespit. Departman
        // bazlı, UNIQUE değil (uygulama katmanı kontrol eder; index yalnız arama hızı için).
        builder.Property(d => d.ContentHash).HasMaxLength(64);
        builder.HasIndex(d => new { d.DepartmentId, d.ContentHash });
    }
}
