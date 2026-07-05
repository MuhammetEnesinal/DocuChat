namespace DocuChat.Application.Interfaces.Services.Persistence;

/// <summary>
/// DB provider-spesifik exception kategorilerini Application'a sızdırmadan tespit etmek için
/// kullanılır. Implementasyon Infrastructure'da (Npgsql tipini gerçek tipiyle pattern match
/// eder); Application reflection veya provider tipine bağımlılık taşımaz.
/// </summary>
public interface IDbExceptionInspector
{
    /// <summary>
    /// PostgreSQL UNIQUE constraint ihlali mi (SQLState 23505).
    /// EF Core DbUpdateException içine sarılı PostgresException'ı da yakalar.
    /// </summary>
    bool IsUniqueConstraintViolation(Exception ex);
}
