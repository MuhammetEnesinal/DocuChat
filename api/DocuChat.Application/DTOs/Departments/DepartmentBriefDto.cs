namespace DocuChat.Application.DTOs.Departments;

// Kullanıcı yanıtlarında departman gösterimi için hafif DTO (Id + Ad + Kod).
public class DepartmentBriefDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
}
