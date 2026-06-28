using Mapster;
using DocuChat.Application.DTOs.Auth;

namespace DocuChat.Infrastructure.Persistence.Identity;

public static class IdentityMappingConfig
{
    public static void Register()
    {
        TypeAdapterConfig<AppUser, UserSummaryResponseDto>.NewConfig()
            .Map(d => d.Email, s => s.Email ?? string.Empty)
            .Map(d => d.FullName, s => s.FullName ?? string.Empty)
            .Map(d => d.Roles, _ => Enumerable.Empty<string>());

        TypeAdapterConfig<AppUser, AuthResponseDto>.NewConfig()
            .Map(d => d.UserId, s => s.Id)
            .Map(d => d.Email, s => s.Email ?? string.Empty)
            .Map(d => d.FullName, s => s.FullName ?? string.Empty)
            .Map(d => d.Token, _ => string.Empty)
            .Map(d => d.ExpiresAt, _ => default(DateTime))
            .Map(d => d.Roles, _ => Enumerable.Empty<string>());
    }
}
