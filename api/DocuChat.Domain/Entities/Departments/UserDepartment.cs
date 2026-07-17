namespace DocuChat.Domain.Entities.Departments;

// Kullanıcı ↔ Departman çoklu üyelik ara tablosu. AppUser (Identity/Infrastructure) katmanına
// Domain bağımlılığı olmasın diye kullanıcı yalnız UserId (string) ile tutulur; AppUser FK'si
// Infrastructure config'de HasOne<AppUser>().WithMany() ile bağlanır (Document deseni ile aynı).
public class UserDepartment
{
    public string UserId { get; set; } = string.Empty;

    public Guid DepartmentId { get; set; }
    public Department? Department { get; set; }
}
