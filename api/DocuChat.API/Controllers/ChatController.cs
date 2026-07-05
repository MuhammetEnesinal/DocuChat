using System.Text.Json;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using DocuChat.Application.Common.Results;
using DocuChat.Application.Interfaces.UseCases;
using DocuChat.Application.DTOs.Chat;
using DocuChat.API.Common;

namespace DocuChat.API.Controllers;

[ApiController]
[Route("api/chat")]
[Authorize]
public class ChatController : ControllerBase
{
    private readonly IChatUseCase _chat;
    private readonly IValidator<AskRequestDto> _askValidator;
    private readonly IValidator<RenameSessionRequestDto> _renameValidator;
    private readonly IValidator<BatchSessionDeleteRequestDto> _batchDeleteValidator;
    private readonly IValidator<FeedbackRequestDto> _feedbackValidator;

    public ChatController(
        IChatUseCase chat,
        IValidator<AskRequestDto> askValidator,
        IValidator<RenameSessionRequestDto> renameValidator,
        IValidator<BatchSessionDeleteRequestDto> batchDeleteValidator,
        IValidator<FeedbackRequestDto> feedbackValidator)
    {
        _chat = chat;
        _askValidator = askValidator;
        _renameValidator = renameValidator;
        _batchDeleteValidator = batchDeleteValidator;
        _feedbackValidator = feedbackValidator;
    }

    // Streaming variant — Server-Sent Events (SSE). Her event "data: <json>\n\n" formatında.
    // Frontend EventSource yerine fetch + ReadableStream kullanır (POST body için).
    [HttpPost("ask-stream")]
    [EnableRateLimiting("chat-ask")]
    public async Task AskStream([FromBody] AskRequestDto req, CancellationToken ct)
    {
        var response = Response;
        response.ContentType = "text/event-stream";
        response.Headers.CacheControl = "no-cache, no-transform";
        response.Headers["X-Accel-Buffering"] = "no";  // nginx/proxy buffering kapat
        response.StatusCode = StatusCodes.Status200OK;

        // KRİTİK: ASP.NET response buffer'ı kapat — yoksa FlushAsync etkisiz, tüm response
        // bir kerede gelir (streaming olmaz).
        HttpContext.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();

        // Validation: hata varsa "error" event olarak gönder ve kapat
        var validation = await _askValidator.ValidateAsync(req, ct);
        if (!validation.IsValid)
        {
            var firstError = validation.Errors.FirstOrDefault()?.ErrorMessage ?? "Geçersiz istek";
            await WriteSseAsync(response, new { type = "error", message = firstError }, ct);
            await WriteSseAsync(response, new { type = "done" }, ct);
            return;
        }

        try
        {
            await foreach (var evt in _chat.AskStreamAsync(req, ct))
            {
                await WriteSseAsync(response, evt, ct);
                if (ct.IsCancellationRequested) break;
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // İstemci bağlantıyı kapattı — normal akış
        }
        catch (Exception ex)
        {
            // Beklenmedik hata → "error" event gönder, normal kapat
            try
            {
                await WriteSseAsync(response, new { type = "error", message = ex.Message }, CancellationToken.None);
                await WriteSseAsync(response, new { type = "done" }, CancellationToken.None);
            }
            catch { /* response zaten kapalı olabilir */ }
        }
    }

    private static async Task WriteSseAsync(HttpResponse response, object payload, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(payload, _sseJsonOptions);
        // SSE format: "data: <json>\n\n"
        await response.WriteAsync($"data: {json}\n\n", ct);
        await response.Body.FlushAsync(ct);
    }

    private static readonly JsonSerializerOptions _sseJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

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
        [FromQuery] bool? archived = false,
        CancellationToken ct = default)
    {
        // archived parametresi her zaman filter olarak iletilir (varsayılan: aktif sessionlar).
        // Frontend "Arşiv" view'inde archived=true gönderir.
        if (page.HasValue)
        {
            var filtered = await _chat.GetMySessionsFilteredAsync(
                page.Value, pageSize, dateFrom, dateTo, sortBy, ascending, archived, ct);
            return filtered.ToActionResult();
        }
        // Sayfasız (tüm sessionlar) — default Get (archived=false aktifler)
        if (archived == true)
        {
            // Arşiv view sayfasız da olabilir → spec ile çek
            var filtered = await _chat.GetMySessionsFilteredAsync(
                1, 1000, dateFrom, dateTo, sortBy, ascending, true, ct);
            return filtered.ToActionResult();
        }
        var result = await _chat.GetMySessionsAsync(ct);
        return result.ToActionResult();
    }

    [HttpGet("sessions/archived-count")]
    [ProducesResponseType(typeof(ApiResponse<int>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetArchivedCount(CancellationToken ct)
    {
        var result = await _chat.GetArchivedCountAsync(ct);
        return result.ToActionResult();
    }

    // ── Archive / Unarchive ──
    [HttpPatch("sessions/{sessionId:guid}/archive")]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ArchiveSession(Guid sessionId, CancellationToken ct)
        => (await _chat.ArchiveSessionAsync(sessionId, ct)).ToActionResult();

    [HttpPatch("sessions/{sessionId:guid}/unarchive")]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> UnarchiveSession(Guid sessionId, CancellationToken ct)
        => (await _chat.UnarchiveSessionAsync(sessionId, ct)).ToActionResult();

    // ── Pin / Unpin ──
    [HttpPatch("sessions/{sessionId:guid}/pin")]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> PinSession(Guid sessionId, CancellationToken ct)
        => (await _chat.PinSessionAsync(sessionId, ct)).ToActionResult();

    [HttpPatch("sessions/{sessionId:guid}/unpin")]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> UnpinSession(Guid sessionId, CancellationToken ct)
        => (await _chat.UnpinSessionAsync(sessionId, ct)).ToActionResult();


    [HttpGet("sessions/{sessionId:guid}/messages")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<ChatMessageResponseDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetMessages(
        Guid sessionId, [FromQuery] int? page, [FromQuery] int pageSize = 50, CancellationToken ct = default)
    {
        if (page.HasValue)
        {
            var paged = await _chat.GetMessagesPagedAsync(sessionId, page.Value, pageSize, ct);
            return paged.ToActionResult();
        }
        var result = await _chat.GetMessagesAsync(sessionId, ct);
        return result.ToActionResult();
    }

    [HttpPatch("sessions/{sessionId:guid}")]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> RenameSession(
        Guid sessionId, [FromBody] RenameSessionRequestDto req, CancellationToken ct)
    {
        var validation = await _renameValidator.ValidateAsync(req, ct);
        if (!validation.IsValid)
            return validation.Errors.Select(e => e.ErrorMessage).ToValidationResult<bool>();

        var result = await _chat.RenameSessionAsync(sessionId, req.Title, ct);
        return result.ToActionResult();
    }

    [HttpDelete("sessions/{sessionId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> DeleteSession(Guid sessionId, CancellationToken ct)
    {
        var result = await _chat.DeleteSessionAsync(sessionId, ct);
        return result.ToNoContentResult();
    }

    [HttpPost("sessions/batch-delete")]
    [EnableRateLimiting("batch-delete")]
    [ProducesResponseType(typeof(ApiResponse<int>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> DeleteSessionsBatch(
        [FromBody] BatchSessionDeleteRequestDto req, CancellationToken ct)
    {
        var validation = await _batchDeleteValidator.ValidateAsync(req, ct);
        if (!validation.IsValid)
            return validation.Errors.Select(e => e.ErrorMessage).ToValidationResult<int>();

        var result = await _chat.DeleteSessionsBatchAsync(req.Ids, ct);
        return result.ToActionResult();
    }

    [HttpGet("popular-questions")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<string>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetPopularQuestions(
        [FromQuery] int limit = 6, CancellationToken ct = default)
    {
        var result = await _chat.GetPopularQuestionsAsync(limit, ct);
        return result.ToActionResult();
    }

    /// <summary>
    /// Bir asistan mesajına 👍 / 👎 + sebep gönderir. Kullanıcı başına bir mesaja yalnız 1 feedback.
    /// </summary>
    [HttpPost("feedback")]
    [EnableRateLimiting("feedback")]
    [ProducesResponseType(typeof(ApiResponse<FeedbackResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> AddFeedback(
        [FromBody] FeedbackRequestDto req, CancellationToken ct)
    {
        var validation = await _feedbackValidator.ValidateAsync(req, ct);
        if (!validation.IsValid)
            return validation.Errors
                             .Select(e => e.ErrorMessage)
                             .ToValidationResult<FeedbackResponseDto>();

        var result = await _chat.AddFeedbackAsync(req, ct);
        return result.ToActionResult();
    }
}
