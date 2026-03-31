using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using DocuChat.Domain.Entities;
using DocuChat.Infrastructure.Identity;

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
    }
}