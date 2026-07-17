using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using DocuChat.Domain.Entities.Departments;
using DocuChat.Infrastructure.Persistence.Identity;

namespace DocuChat.Infrastructure.Persistence.Configurations.Departments;

public class UserDepartmentConfiguration : IEntityTypeConfiguration<UserDepartment>
{
    public void Configure(EntityTypeBuilder<UserDepartment> builder)
    {
        // Composite PK — aynı kullanıcı aynı departmana iki kez üye olamaz.
        builder.HasKey(ud => new { ud.UserId, ud.DepartmentId });

        builder.HasOne(ud => ud.Department)
               .WithMany(d => d.UserDepartments)
               .HasForeignKey(ud => ud.DepartmentId)
               .OnDelete(DeleteBehavior.Cascade);

        // AppUser FK'si — Domain'in Identity'ye bağımlı olmaması için nav Domain'de yok,
        // ilişki buradan bağlanır (Document deseni ile aynı).
        builder.HasOne<AppUser>()
               .WithMany(u => u.UserDepartments)
               .HasForeignKey(ud => ud.UserId)
               .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(ud => ud.DepartmentId);
    }
}
