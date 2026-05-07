using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using DocuChat.Application.Common;
using DocuChat.Application.Interfaces.Services;
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
    private readonly IValidator<RenameSessionRequest> _renameValidator;
    private readonly IValidator<BatchDeleteRequest> _batchDeleteValidator;

    public ChatController(
        IChatService chatService,
        IValidator<AskRequest> askValidator,
        IValidator<RenameSessionRequest> renameValidator,
        IValidator<BatchDeleteRequest> batchDeleteValidator)
    {
        _chatService = chatService;
        _askValidator = askValidator;
        _renameValidator = renameValidator;
        _batchDeleteValidator = batchDeleteValidator;
    }

    [HttpPost("ask")]
    [EnableRateLimiting("chat-ask")]
    [ProducesResponseType(typeof(ApiResponse<AskResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Ask([FromBody] AskRequest req, CancellationToken ct)
    {
        var validation = await _askValidator.ValidateAsync(req, ct);
        if (!validation.IsValid)
            return validation.Errors.Select(e => e.ErrorMessage).ToValidationResult<AskResponseDto>();

        var result = await _chatService.AskAsync(req, ct);
        return result.ToActionResult();
    }

    [HttpGet("sessions")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<ChatSessionResponseDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetMySessions(
        [FromQuery] int? page,
        [FromQuery] int pageSize = 20,
        [FromQuery] DateTime? dateFrom = null,
        [FromQuery] DateTime? dateTo = null,
        [FromQuery] string sortBy = "createdAt",
        [FromQuery] bool ascending = false,
        CancellationToken ct = default)
    {
        if (page.HasValue)
        {
            var hasFilter = dateFrom.HasValue || dateTo.HasValue
                || sortBy != "createdAt" || ascending;

            if (hasFilter)
            {
                var filtered = await _chatService.GetMySessionsFilteredAsync(
                    page.Value, pageSize, dateFrom, dateTo, sortBy, ascending, ct);
                return filtered.ToActionResult();
            }

            var paged = await _chatService.GetMySessionsPagedAsync(page.Value, pageSize, ct);
            return paged.ToActionResult();
        }
        var result = await _chatService.GetMySessionsAsync(ct);
        return result.ToActionResult();
    }

    [HttpGet("sessions/{sessionId:guid}/messages")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<ChatMessageResponseDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetMessages(
        Guid sessionId, [FromQuery] int? page, [FromQuery] int pageSize = 50, CancellationToken ct = default)
    {
        if (page.HasValue)
        {
            var paged = await _chatService.GetMessagesPagedAsync(sessionId, page.Value, pageSize, ct);
            return paged.ToActionResult();
        }
        var result = await _chatService.GetMessagesAsync(sessionId, ct);
        return result.ToActionResult();
    }

    [HttpPatch("sessions/{sessionId:guid}")]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> RenameSession(
        Guid sessionId, [FromBody] RenameSessionRequest req, CancellationToken ct)
    {
        var validation = await _renameValidator.ValidateAsync(req, ct);
        if (!validation.IsValid)
            return validation.Errors.Select(e => e.ErrorMessage).ToValidationResult<bool>();

        var result = await _chatService.RenameSessionAsync(sessionId, req.Title, ct);
        return result.ToActionResult();
    }

    [HttpDelete("sessions/{sessionId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> DeleteSession(Guid sessionId, CancellationToken ct)
    {
        var result = await _chatService.DeleteSessionAsync(sessionId, ct);
        return result.ToNoContentResult();
    }

    [HttpPost("sessions/batch-delete")]
    [ProducesResponseType(typeof(ApiResponse<int>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> DeleteSessionsBatch(
        [FromBody] BatchDeleteRequest req, CancellationToken ct)
    {
        var validation = await _batchDeleteValidator.ValidateAsync(req, ct);
        if (!validation.IsValid)
            return validation.Errors.Select(e => e.ErrorMessage).ToValidationResult<int>();

        var result = await _chatService.DeleteSessionsBatchAsync(req.Ids, ct);
        return result.ToActionResult();
    }

    [HttpGet("popular-questions")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<string>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetPopularQuestions(
        [FromQuery] int limit = 6, CancellationToken ct = default)
    {
        var result = await _chatService.GetPopularQuestionsAsync(limit, ct);
        return result.ToActionResult();
    }
}
