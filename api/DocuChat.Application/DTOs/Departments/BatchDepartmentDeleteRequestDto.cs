namespace DocuChat.Application.DTOs.Departments;

public class BatchDepartmentDeleteRequestDto
{
    public IEnumerable<Guid> Ids { get; set; }

    public BatchDepartmentDeleteRequestDto(IEnumerable<Guid> Ids)
    {
        this.Ids = Ids;
    }
}
