using DocuChat.Application.Interfaces.Services.Ai.Embedding;
using DocuChat.Application.Interfaces.Services.Ai.Llm;
using DocuChat.Application.Interfaces.Services.Ai.Reranker;
using DocuChat.Application.Interfaces.Services.Ai.Retrieval;
using DocuChat.Application.Interfaces.Services.Documents;
using DocuChat.Application.Interfaces.Services.Auth;
using DocuChat.Application.Interfaces.Services.UserManagement;
using DocuChat.Application.Interfaces.Services.Email;
using DocuChat.Application.Interfaces.Services.Storage;
using DocuChat.Application.Interfaces.Services.Persistence;
using Npgsql;

namespace DocuChat.Infrastructure.Persistence.Exceptions;

// PostgreSQL/Npgsql exception inspeksiyonu. Application IDbExceptionInspector üzerinden
// kullanır → Application'a Npgsql tipi sızmaz.
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
