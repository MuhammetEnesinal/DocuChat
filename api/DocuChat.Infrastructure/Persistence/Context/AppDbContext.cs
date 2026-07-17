using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using DocuChat.Domain.Entities;
using DocuChat.Domain.Entities.Common;
using DocuChat.Domain.Entities.Chat;
using DocuChat.Domain.Entities.Documents;
using DocuChat.Domain.Entities.Caching;
using DocuChat.Domain.Entities.Departments;
using DocuChat.Infrastructure.Persistence.Identity;

namespace DocuChat.Infrastructure.Persistence.Context;

public class AppDbContext : IdentityDbContext<AppUser, AppRole, string>
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Document> Documents { get; set; }
    public DbSet<DocumentChunk> DocumentChunks { get; set; }
    public DbSet<DocumentImage> DocumentImages { get; set; }
    public DbSet<ChunkImage> ChunkImages { get; set; }
    public DbSet<ChatSession> ChatSessions { get; set; }
    public DbSet<ChatMessage> ChatMessages { get; set; }
    public DbSet<ChatMessageFeedback> ChatMessageFeedbacks { get; set; }
    public DbSet<QuestionCache> QuestionCaches { get; set; }
    public DbSet<Department> Departments { get; set; }
    public DbSet<UserDepartment> UserDepartments { get; set; }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.HasPostgresExtension("vector");
        builder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        // Personel kodu benzersiz olmalı. Nullable — PostgreSQL'de null değerler çakışmaz.
        builder.Entity<AppUser>()
            .HasIndex(u => u.PersonnelCode)
            .IsUnique();
    }
}
