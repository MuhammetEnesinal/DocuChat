using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using DocuChat.Application.Interfaces.Services;
using DocuChat.Application.Interfaces.Repositories;
using DocuChat.Application.DTOs.Auth;
using DocuChat.API.Extensions;
using DocuChat.Domain.Enums;

namespace DocuChat.API.Controllers;

[ApiController]
[Route("api/admin")]
[Authorize(Roles = Roles.Admin)]
public class AdminController : ControllerBase
{
    private readonly IDocumentService _documentService;
    private readonly IChatService _chatService;
    private readonly IAuthService _authService;
    private readonly IValidator<RegisterRequest> _registerValidator;

    public AdminController(
        IDocumentService documentService,
        IChatService chatService,
        IAuthService authService,
        IValidator<RegisterRequest> registerValidator)
    {
        _documentService = documentService;
        _chatService = chatService;
        _authService = authService;
        _registerValidator = registerValidator;
    }

    // ───── KULLANICILAR ─────

    [HttpGet("users")]
    public async Task<IActionResult> GetAllUsers(CancellationToken ct)
    {
        var result = await _authService.GetAllUsersAsync(ct);
        return result.ToActionResult();
    }

    [HttpPost("users")]
    public async Task<IActionResult> CreateUser([FromBody] RegisterRequest req, CancellationToken ct)
    {
        var validation = await _registerValidator.ValidateAsync(req, ct);
        if (!validation.IsValid)
            return validation.Errors
                             .Select(e => e.ErrorMessage)
                             .ToValidationResult<AuthResponseDto>();

        var result = await _authService.RegisterAsync(req, ct);
        return result.ToActionResult();
    }

    [HttpDelete("users/{id}")]
    public async Task<IActionResult> DeleteUser(string id, CancellationToken ct)
    {
        var result = await _authService.DeleteUserAsync(id, ct);
        return result.ToActionResult();
    }

    // ───── BELGELER ─────

    [HttpGet("documents")]
    public async Task<IActionResult> GetAllDocuments(CancellationToken ct)
    {
        var result = await _documentService.GetAllDocumentsAsync(ct);
        return result.ToActionResult();
    }

    [HttpDelete("documents/{id:guid}")]
    public async Task<IActionResult> DeleteDocument(Guid id, CancellationToken ct)
    {
        var result = await _documentService.DeleteAsync(id, ct);
        return result.ToActionResult();
    }

    // ───── OTURUMLAR ─────

    [HttpDelete("sessions/{sessionId:guid}")]
    public async Task<IActionResult> DeleteSession(Guid sessionId, CancellationToken ct)
    {
        var result = await _chatService.DeleteSessionAsync(sessionId, ct);
        return result.ToActionResult();
    }
}