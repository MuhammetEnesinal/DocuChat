namespace DocuChat.Application.Abstractions;

public record TokenResult(string Token, DateTime ExpiresAt);

public interface IJwtTokenService
{
    TokenResult GenerateToken(string userId, string email, IList<string> roles);
}