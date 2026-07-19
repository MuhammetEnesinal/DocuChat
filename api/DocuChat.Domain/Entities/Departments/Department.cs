using DocuChat.Domain.Entities.Common;
using DocuChat.Domain.Entities.Documents;

namespace DocuChat.Domain.Entities.Departments;

// Admin tarafından oluşturulan departman. Belgeler ve kullanıcılar departmana bağlanır;
// arama/erişim izolasyonunun temel birimidir (kesin izolasyon: departman dışına veri sızmaz).
public class Department : BaseEntity
{
    public string Name { get; set; } = string.Empty;

    // Kısa departman kodu (örneğin "YAZILIM", "IK"). Zorunlu ve benzersizdir. Excel ile toplu
    // kullanıcı aktarımında departman eşleştirmesi yalnızca bu kod üzerinden yapılır; departman
    // adı yazılan satırlar atlanır. Arayüzde "Ad - KOD" biçiminde gösterilir.
    // İzolasyon mantığına DAHİL DEĞİLDİR — arama/cache/yetki her zaman DepartmentId üzerinden.
    public string Code { get; set; } = string.Empty;

    // Bu departmana yüklenmiş belgeler (Document.DepartmentId FK).
    public List<Document> Documents { get; set; } = new();

    // Kullanıcı üyelikleri (çoklu: bir departmanda N kullanıcı, bir kullanıcı N departman).
    public List<UserDepartment> UserDepartments { get; set; } = new();
}
