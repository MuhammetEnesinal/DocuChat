namespace DocuChat.Application.DTOs.Departments;

public class DepartmentResponseDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }

    // Admin UI'da gösterim + silme koruması mesajı için.
    public int UserCount { get; set; }
    public int DocumentCount { get; set; }
}
