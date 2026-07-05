using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using DocuChat.Domain.Entities;
using DocuChat.Domain.Entities.Common;
using DocuChat.Domain.Entities.Chat;
using DocuChat.Domain.Entities.Documents;
using DocuChat.Domain.Entities.Caching;

namespace DocuChat.Infrastructure.Persistence.Configurations.Documents;

public class DocumentImageConfiguration : IEntityTypeConfiguration<DocumentImage>
{
    public void Configure(EntityTypeBuilder<DocumentImage> b)
    {
        b.HasKey(x => x.Id);

        b.Property(x => x.Path).IsRequired().HasMaxLength(1024);
        b.Property(x => x.ContentHash).HasMaxLength(64);

        b.HasIndex(x => x.DocumentId);
        b.HasIndex(x => x.ContentHash);

        b.HasOne(x => x.Document)
         .WithMany(d => d.Images)
         .HasForeignKey(x => x.DocumentId)
         .OnDelete(DeleteBehavior.Cascade);
    }
}
