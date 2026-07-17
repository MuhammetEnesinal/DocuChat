using DocuChat.Application.DTOs.Departments;

namespace DocuChat.Application.DTOs.Auth;

public class UserSummaryResponseDto
{
    public string Id { get; set; }
    public string Email { get; set; }
    public string FullName { get; set; }
    public string? PersonnelCode { get; set; }
    public DateTime CreatedAt { get; set; }
    public IEnumerable<string> Roles { get; set; }

    // Kullanıcının atandığı departmanlar (Id + Ad). Servis tarafından Adapt sonrası doldurulur.
    public IEnumerable<DepartmentBriefDto> Departments { get; set; } = new List<DepartmentBriefDto>();

    public UserSummaryResponseDto(
        string Id, string Email, string FullName, DateTime CreatedAt, IEnumerable<string> Roles,
        string? PersonnelCode = null)
    {
        this.Id = Id;
        this.Email = Email;
        this.FullName = FullName;
        this.CreatedAt = CreatedAt;
        this.Roles = Roles;
        this.PersonnelCode = PersonnelCode;
    }
}
