namespace DocuChat.Application.Interfaces.Services.Persistence;

// DB provider-spesifik exception kategorilerini Application'a sızdırmadan tespit etmek için
// kullanılır. Implementasyon Infrastructure'da (Npgsql tipini gerçek tipiyle pattern match
// eder); Application reflection veya provider tipine bağımlılık taşımaz.
public interface IDbExceptionInspector
{
    // PostgreSQL UNIQUE constraint ihlali mi (SQLState 23505).
    // EF Core DbUpdateException içine sarılı PostgresException'ı da yakalar.
    bool IsUniqueConstraintViolation(Exception ex);
}
