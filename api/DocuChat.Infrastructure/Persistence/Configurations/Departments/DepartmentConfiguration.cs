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

        // Ad ve kod benzersiz — admin aynısını iki kez ekleyemez. (Uygulama katmanı ayrıca
        // Türkçe-duyarlı normalleştirmeyle yakın-mükerrerleri de engeller: "YAZILIM" vs "Yazılım".)
        builder.HasIndex(d => d.Name).IsUnique();
        builder.HasIndex(d => d.Code).IsUnique();
    }
}
