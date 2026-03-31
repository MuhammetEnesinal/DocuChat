using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using DocuChat.Application.Abstractions;
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

    public AdminController(
        IDocumentService documentService,
        IChatService chatService,
        IAuthService authService)
    {
        _documentService = documentService;
        _chatService = chatService;
        _authService = authService;
    }

    // ───── KULLANICILAR ─────

    [HttpGet("users")]
    public async Task<IActionResult> GetAllUsers(CancellationToken ct)
    {
        var result = await _authService.GetAllUsersAsync(ct);
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