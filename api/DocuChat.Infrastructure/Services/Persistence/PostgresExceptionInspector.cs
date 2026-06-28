using DocuChat.Application.Interfaces.Services;
using Npgsql;

namespace DocuChat.Infrastructure.Services.Persistence;

/// <summary>
/// PostgreSQL/Npgsql exception inspeksiyonu. Application IDbExceptionInspector üzerinden
/// kullanır → Application'a Npgsql tipi sızmaz.
/// </summary>
public sealed class PostgresExceptionInspector : IDbExceptionInspector
{
    // PostgreSQL SQLSTATE 23505 = unique_violation
    private const string UniqueViolationSqlState = "23505";

    public bool IsUniqueConstraintViolation(Exception ex)
    {
        var inner = ex.InnerException ?? ex;
        return inner is PostgresException pg && pg.SqlState == UniqueViolationSqlState;
    }
}
