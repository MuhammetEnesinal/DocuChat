// DocuChat.API/Controllers/DocumentsController.cs
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using DocuChat.Application.Common;
using DocuChat.Application.Interfaces.UseCases;
using DocuChat.Application.DTOs.Document;
using DocuChat.API.Extensions;
using DocuChat.Domain.Enums;

namespace DocuChat.API.Controllers;

public class UploadFileRequest
{
    public IFormFile File { get; set; } = null!;
}

[ApiController]
[Route("api/documents")]
[Authorize(Roles = Roles.Admin)]
public class DocumentsController : ControllerBase
{
    private readonly IDocumentUseCase _document;
    private readonly IValidator<UploadDocumentRequest> _uploadValidator;
    private readonly IValidator<BatchDocumentDeleteRequest> _batchDeleteValidator;

    public DocumentsController(
        IDocumentUseCase document,
        IValidator<UploadDocumentRequest> uploadValidator,
        IValidator<BatchDocumentDeleteRequest> batchDeleteValidator)
    {
        _document = document;
        _uploadValidator = uploadValidator;
        _batchDeleteValidator = batchDeleteValidator;
    }

    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<DocumentResponseDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetAll(
        [FromQuery] int? page, [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null, CancellationToken ct = default)
    {
        if (page.HasValue)
        {
            var paged = await _document.GetAllDocumentsPagedAsync(page.Value, pageSize, ct);
            return paged.ToActionResult();
        }
        var result = await _document.GetAllDocumentsAsync(search, ct);
        return result.ToActionResult();
    }

    [HttpGet("{id:guid}/chunks")]
    public async Task<IActionResult> GetChunks(Guid id, CancellationToken ct)
    {
        var result = await _document.GetChunksAsync(id, ct);
        return result.ToActionResult();
    }

    [HttpPost("upload")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(ApiResponse<DocumentResponseDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Upload(
        [FromForm] UploadFileRequest request, CancellationToken ct)
    {
        var file = request.File;
        var req = new UploadDocumentRequest(
            file.FileName, file.ContentType, file.Length, file.OpenReadStream());

        var validation = await _uploadValidator.ValidateAsync(req, ct);
        if (!validation.IsValid)
            return validation.Errors
                             .Select(e => e.ErrorMessage)
                             .ToValidationResult<DocumentResponseDto>();

        var result = await _document.UploadAsync(req, ct);
        return result.ToCreatedResult();
    }

    [HttpGet("{id:guid}/preview")]
    public async Task<IActionResult> Preview(Guid id, CancellationToken ct)
    {
        var result = await _document.GetFileStreamAsync(id, ct);
        if (!result.IsSuccess)
            return result.ToActionResult();

        var (stream, contentType, fileName) = result.Value;
        Response.Headers["Content-Disposition"] =
            $"inline; filename=\"{Uri.EscapeDataString(fileName)}\"";
        Response.RegisterForDispose(stream);

        return File(stream, contentType, enableRangeProcessing: true);
    }

    [HttpPost("{id:guid}/reprocess")]
    public async Task<IActionResult> Reprocess(Guid id, CancellationToken ct)
    {
        var result = await _document.ReprocessAsync(id, ct);
        return result.ToActionResult();
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var result = await _document.DeleteAsync(id, ct);
        return result.ToNoContentResult();
    }

    [HttpPost("batch-delete")]
    [ProducesResponseType(typeof(ApiResponse<int>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> DeleteBatch(
        [FromBody] BatchDocumentDeleteRequest req, CancellationToken ct)
    {
        var validation = await _batchDeleteValidator.ValidateAsync(req, ct);
        if (!validation.IsValid)
            return validation.Errors.Select(e => e.ErrorMessage).ToValidationResult<int>();

        var result = await _document.DeleteBatchAsync(req.Ids, ct);
        return result.ToActionResult();
    }
}
