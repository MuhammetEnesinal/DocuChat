using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using DocuChat.Domain.Entities.Departments;

namespace DocuChat.Infrastructure.Persistence.Configurations.Departments;

public class DepartmentConfiguration : IEntityTypeConfiguration<Department>
{
    public void Configure(EntityTypeBuilder<Department> builder)
    {
        builder.HasKey(d => d.Id);

        builder.Property(d => d.Name)
               .IsRequired()
               .HasMaxLength(150);

        builder.Property(d => d.Code)
               .IsRequired()
               .HasMaxLength(20);

        // Ad ve kod benzersizdir. Karşılaştırma birebir yapılır: Türkçe'de İ/I ve ı/i ayrı
        // harfler olduğundan büyük/küçük harf katlaması uygulanmaz, "IT" ile "ıt" farklı
        // kodlardır. Uygulama katmanındaki mükerrer denetimi de aynı semantiği kullanır.
        builder.HasIndex(d => d.Name).IsUnique();
        builder.HasIndex(d => d.Code).IsUnique();
    }
}
