using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using DocuChat.Application.Abstractions;

namespace DocuChat.Infrastructure.Identity;

public class CurrentUserService : ICurrentUser
{
    private readonly IHttpContextAccessor _http;

    public CurrentUserService(IHttpContextAccessor http) => _http = http;

    private ClaimsPrincipal User => _http.HttpContext!.User;

    public string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;
    public string Email => User.FindFirstValue(ClaimTypes.Email)!;
    public bool IsAuthenticated => User.Identity?.IsAuthenticated ?? false;
    public bool IsInRole(string role) => User.IsInRole(role);
}