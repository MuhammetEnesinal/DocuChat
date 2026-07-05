
namespace DocuChat.Application.Interfaces.Services.Auth;

public interface ICurrentUser
{
    string UserId { get; }
    string Email { get; }
    bool IsAuthenticated { get; }
    bool IsInRole(string role);
}
