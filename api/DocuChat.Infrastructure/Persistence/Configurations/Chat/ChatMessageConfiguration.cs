using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using DocuChat.Domain.Entities;
using DocuChat.Domain.Entities.Common;
using DocuChat.Domain.Entities.Chat;
using DocuChat.Domain.Entities.Documents;
using DocuChat.Domain.Entities.Caching;

namespace DocuChat.Infrastructure.Persistence.Configurations.Chat;

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

        // Self-reference FK: assistant message → user message link
        // SetNull: user mesajı silinirse assistant message kalır, FK null'a düşer
        builder.HasOne(m => m.ResponseToMessage)
               .WithMany()
               .HasForeignKey(m => m.ResponseToMessageId)
               .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(m => m.ResponseToMessageId);
    }
}
