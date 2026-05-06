using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using DocuChat.Application.Interfaces.Services;
using DocuChat.Application.DTOs.Auth;
using DocuChat.API.Extensions;

namespace DocuChat.API.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;
    private readonly IValidator<LoginRequest> _loginValidator;

    public AuthController(
        IAuthService authService,
        IValidator<LoginRequest> loginValidator)
    {
        _authService = authService;
        _loginValidator = loginValidator;
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest req, CancellationToken ct)
    {
        var validation = await _loginValidator.ValidateAsync(req, ct);
        if (!validation.IsValid)
            return validation.Errors
                             .Select(e => e.ErrorMessage)
                             .ToValidationResult<AuthResponseDto>();

        var result = await _authService.LoginAsync(req, ct);
        return result.ToActionResult();
    }
}