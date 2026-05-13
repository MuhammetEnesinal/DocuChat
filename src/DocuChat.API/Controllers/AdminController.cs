using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using DocuChat.Application.Interfaces.Services;
using DocuChat.Application.DTOs.Auth;
using DocuChat.Application.DTOs.Chat;
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
    private readonly IValidator<UpdateUserRequest> _updateUserValidator;

    public AdminController(
        IDocumentService documentService,
        IChatService chatService,
        IAuthService authService,
        IValidator<RegisterRequest> registerValidator,
        IValidator<UpdateUserRequest> updateUserValidator)
    {
        _documentService = documentService;
        _chatService = chatService;
        _authService = authService;
        _registerValidator = registerValidator;
        _updateUserValidator = updateUserValidator;
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
        return result.ToCreatedResult();
    }

    [HttpPut("users/{id}")]
    public async Task<IActionResult> UpdateUser(string id, [FromBody] UpdateUserRequest req, CancellationToken ct)
    {
        var validation = await _updateUserValidator.ValidateAsync(req, ct);
        if (!validation.IsValid)
            return validation.Errors
                             .Select(e => e.ErrorMessage)
                             .ToValidationResult<UserSummaryResponseDto>();

        var result = await _authService.UpdateUserAsync(id, req, ct);
        return result.ToActionResult();
    }

    [HttpDelete("users/{id}")]
    public async Task<IActionResult> DeleteUser(string id, CancellationToken ct)
    {
        var result = await _authService.DeleteUserAsync(id, ct);
        return result.ToNoContentResult();
    }

    // ───── BELGELER ─────

    [HttpGet("documents")]
    public async Task<IActionResult> GetAllDocuments(
        [FromQuery] int? page, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        if (page.HasValue)
        {
            var paged = await _documentService.GetAllDocumentsPagedAsync(page.Value, pageSize, ct);
            return paged.ToActionResult();
        }
        var result = await _documentService.GetAllDocumentsAsync(null, ct);
        return result.ToActionResult();
    }

    [HttpDelete("documents/{id:guid}")]
    public async Task<IActionResult> DeleteDocument(Guid id, CancellationToken ct)
    {
        var result = await _documentService.DeleteAsync(id, ct);
        return result.ToNoContentResult();
    }

    [HttpPost("documents/batch-delete")]
    public async Task<IActionResult> DeleteDocumentsBatch(
        [FromBody] BatchDocumentDeleteRequest req, CancellationToken ct)
    {
        var result = await _documentService.DeleteBatchAsync(req.Ids, ct);
        return result.ToActionResult();
    }

    // ───── OTURUMLAR ─────

    [HttpDelete("sessions/{sessionId:guid}")]
    public async Task<IActionResult> DeleteSession(Guid sessionId, CancellationToken ct)
    {
        var result = await _chatService.DeleteSessionAsync(sessionId, ct);
        return result.ToNoContentResult();
    }

    [HttpPost("sessions/batch-delete")]
    public async Task<IActionResult> DeleteSessionsBatch(
        [FromBody] BatchDeleteRequest req, CancellationToken ct)
    {
        var result = await _chatService.DeleteSessionsBatchAsync(req.Ids, ct);
        return result.ToActionResult();
    }
}
