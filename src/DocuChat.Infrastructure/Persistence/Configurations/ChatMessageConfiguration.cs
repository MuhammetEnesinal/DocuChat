using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using DocuChat.Domain.Entities;

namespace DocuChat.Infrastructure.Persistence.Configurations;

public class ChatMessageConfiguration : IEntityTypeConfiguration<ChatMessage>
{
    public void Configure(EntityTypeBuilder<ChatMessage> builder)
    {
        builder.HasKey(m => m.Id);

        builder.HasOne(m => m.Session)
               .WithMany(s => s.Messages)
               .HasForeignKey(m => m.SessionId)
               .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(m => m.SessionId);
    }
}
