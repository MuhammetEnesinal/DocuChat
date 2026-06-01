using DocuChat.Infrastructure.Persistence.Identity;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace DocuChat.Infrastructure.Services.Auth;

public class JwtTokenService
{
    private readonly IConfiguration _cfg;

    public JwtTokenService(IConfiguration cfg) => _cfg = cfg;

    public string Generate(AppUser user, IList<string> roles)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id),
            new(ClaimTypes.Email,          user.Email!),
            new(ClaimTypes.Name,           user.FullName ?? string.Empty),
        };

        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));

        var secret = _cfg["Jwt:Secret"] ?? throw new InvalidOperationException("Jwt:Secret eksik.");
        var issuer = _cfg["Jwt:Issuer"] ?? throw new InvalidOperationException("Jwt:Issuer eksik.");
        var audience = _cfg["Jwt:Audience"] ?? throw new InvalidOperationException("Jwt:Audience eksik.");
        var expiryHours = _cfg.GetValue<double>("Jwt:ExpiryHours", 24);

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var expires = DateTime.UtcNow.AddHours(expiryHours);

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: expires,
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
