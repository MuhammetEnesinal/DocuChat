using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using DocuChat.Application.Abstractions;
using DocuChat.Application.DTOs.Chat;
using DocuChat.API.Extensions;

namespace DocuChat.API.Controllers;

[ApiController]
[Route("api/chat")]
[Authorize]
public class ChatController : ControllerBase
{
    private readonly IChatService _chatService;
    private readonly IValidator<AskRequest> _askValidator;

    public ChatController(
        IChatService chatService,
        IValidator<AskRequest> askValidator)
    {
        _chatService = chatService;
        _askValidator = askValidator;
    }

    [HttpPost("ask")]
    public async Task<IActionResult> Ask([FromBody] AskRequest req, CancellationToken ct)
    {
        var validation = await _askValidator.ValidateAsync(req, ct);
        if (!validation.IsValid)
            return validation.Errors
                             .Select(e => e.ErrorMessage)
                             .ToValidationResult<AskResponseDto>();

        var result = await _chatService.AskAsync(req, ct);
        return result.ToActionResult();
    }

    [HttpGet("sessions")]
    public async Task<IActionResult> GetMySessions(CancellationToken ct)
    {
        var result = await _chatService.GetMySessionsAsync(ct);
        return result.ToActionResult();
    }

    [HttpGet("sessions/{sessionId:guid}/messages")]
    public async Task<IActionResult> GetMessages(Guid sessionId, CancellationToken ct)
    {
        var result = await _chatService.GetMessagesAsync(sessionId, ct);
        return result.ToActionResult();
    }

    [HttpDelete("sessions/{sessionId:guid}")]
    public async Task<IActionResult> DeleteSession(Guid sessionId, CancellationToken ct)
    {
        var result = await _chatService.DeleteSessionAsync(sessionId, ct);
        return result.ToActionResult();
    }
}