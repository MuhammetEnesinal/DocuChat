using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using DocuChat.Application.Common.Results;
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
using DocuChat.Application.DTOs.Auth;
using DocuChat.API.Common;

namespace DocuChat.API.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    // Static dosyalar (/uploads/*) için JWT cookie ismi.
    // JWT bearer middleware OnMessageReceived event'inde bu cookie'den de okuyor.
    private const string AuthCookieName = "auth_token";

    private readonly IAuthService _authService;
    private readonly ICurrentUser _currentUser;
    private readonly IWebHostEnvironment _env;
    private readonly IValidator<LoginRequestDto> _loginValidator;
    private readonly IValidator<ForgotPasswordRequestDto> _forgotPasswordValidator;
    private readonly IValidator<ResetPasswordRequestDto> _resetPasswordValidator;
    private readonly IValidator<ChangePasswordRequestDto> _changePasswordValidator;

    public AuthController(
        IAuthService authService,
        ICurrentUser currentUser,
        IWebHostEnvironment env,
        IValidator<LoginRequestDto> loginValidator,
        IValidator<ForgotPasswordRequestDto> forgotPasswordValidator,
        IValidator<ResetPasswordRequestDto> resetPasswordValidator,
        IValidator<ChangePasswordRequestDto> changePasswordValidator)
    {
        _authService = authService;
        _currentUser = currentUser;
        _env = env;
        _loginValidator = loginValidator;
        _forgotPasswordValidator = forgotPasswordValidator;
        _resetPasswordValidator = resetPasswordValidator;
        _changePasswordValidator = changePasswordValidator;
    }

    [HttpPost("login")]
    [EnableRateLimiting("login")]
    [ProducesResponseType(typeof(ApiResponse<AuthResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Login([FromBody] LoginRequestDto req, CancellationToken ct)
    {
        var validation = await _loginValidator.ValidateAsync(req, ct);
        if (!validation.IsValid)
            return validation.Errors
                             .Select(e => e.ErrorMessage)
                             .ToValidationResult<AuthResponseDto>();

        var result = await _authService.LoginAsync(req, ct);

        // Login başarılıysa JWT'yi HttpOnly cookie olarak da set et — <img src=/uploads/...>
        // çağrılarında tarayıcı cookie'yi otomatik gönderir (Authorization header gönderemez).
        if (result.IsSuccess)
            SetAuthCookie(result.Value!.Token, result.Value!.ExpiresAt);

        return result.ToActionResult();
    }

    [HttpPost("logout")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public IActionResult Logout()
    {
        Response.Cookies.Delete(AuthCookieName, new CookieOptions
        {
            HttpOnly = true,
            Secure = !_env.IsDevelopment(),
            SameSite = SameSiteMode.Lax,
            Path = "/"
        });
        return NoContent();
    }

    [HttpPost("forgot-password")]
    [EnableRateLimiting("password-reset")]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequestDto req, CancellationToken ct)
    {
        var validation = await _forgotPasswordValidator.ValidateAsync(req, ct);
        if (!validation.IsValid)
            return validation.Errors
                             .Select(e => e.ErrorMessage)
                             .ToValidationResult<bool>();

        var result = await _authService.ForgotPasswordAsync(req.Email, ct);
        return result.ToActionResult();
    }

    [HttpPost("reset-password")]
    [EnableRateLimiting("password-reset")]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequestDto req, CancellationToken ct)
    {
        var validation = await _resetPasswordValidator.ValidateAsync(req, ct);
        if (!validation.IsValid)
            return validation.Errors
                             .Select(e => e.ErrorMessage)
                             .ToValidationResult<bool>();

        var result = await _authService.ResetPasswordAsync(req.Email, req.Token, req.NewPassword, ct);
        return result.ToActionResult();
    }

    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType(typeof(ApiResponse<UserSummaryResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Me(CancellationToken ct)
    {
        var result = await _authService.GetMeAsync(_currentUser.UserId, ct);
        return result.ToActionResult();
    }

    [HttpPost("change-password")]
    [Authorize]
    [EnableRateLimiting("login")]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequestDto req, CancellationToken ct)
    {
        var validation = await _changePasswordValidator.ValidateAsync(req, ct);
        if (!validation.IsValid)
            return validation.Errors
                             .Select(e => e.ErrorMessage)
                             .ToValidationResult<bool>();

        var result = await _authService.ChangePasswordAsync(_currentUser.UserId, req.CurrentPassword, req.NewPassword, ct);
        return result.ToActionResult();
    }

    // JWT'yi HttpOnly cookie olarak set eder.
    // - HttpOnly: JS okuyamaz (XSS koruması)
    // - Secure: prod'da HTTPS şart (dev localhost http için false)
    // - SameSite=Lax: cross-site CSRF saldırılarına karşı koruma; same-site img/link OK
    // - Path=/: tüm endpoint'ler için geçerli
    private void SetAuthCookie(string token, DateTime expiresAt)
    {
        Response.Cookies.Append(AuthCookieName, token, new CookieOptions
        {
            HttpOnly = true,
            Secure = !_env.IsDevelopment(),
            SameSite = SameSiteMode.Lax,
            Expires = expiresAt,
            Path = "/"
        });
    }
}
