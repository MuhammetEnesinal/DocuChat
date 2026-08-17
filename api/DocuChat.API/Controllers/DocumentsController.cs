// DocuChat.API/Controllers/DocumentsController.cs
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using DocuChat.Application.Common.Results;
using DocuChat.Application.Interfaces.UseCases;
using DocuChat.Application.DTOs.Document;
using DocuChat.API.Common;
using DocuChat.Domain.Enums;

namespace DocuChat.API.Controllers;

[ApiController]
[Route("api/documents")]
[Authorize(Roles = $"{Roles.Admin},{Roles.Manager}")]
public class DocumentsController : ControllerBase
{
    private readonly IDocumentUseCase _document;
    private readonly IValidator<UploadDocumentRequestDto> _uploadValidator;
    private readonly IValidator<BatchDocumentDeleteRequestDto> _batchDeleteValidator;
    private readonly IValidator<BatchDocumentReprocessRequestDto> _batchReprocessValidator;

    public DocumentsController(
        IDocumentUseCase document,
        IValidator<UploadDocumentRequestDto> uploadValidator,
        IValidator<BatchDocumentDeleteRequestDto> batchDeleteValidator,
        IValidator<BatchDocumentReprocessRequestDto> batchReprocessValidator)
    {
        _document = document;
        _uploadValidator = uploadValidator;
        _batchDeleteValidator = batchDeleteValidator;
        _batchReprocessValidator = batchReprocessValidator;
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
            var p = Math.Max(1, page.Value);
            var ps = Math.Clamp(pageSize, 1, 100);
            var paged = await _document.GetAllDocumentsPagedAsync(p, ps, search, ct);
            return paged.ToActionResult();
        }
        var result = await _document.GetAllDocumentsAsync(search, ct);
        return result.ToActionResult();
    }

    [HttpPost("upload")]
    [EnableRateLimiting("upload")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(ApiResponse<DocumentResponseDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Upload(
        [FromForm] UploadFileRequest request, CancellationToken ct)
    {
        var file = request.File;
        var req = new UploadDocumentRequestDto(
            file.FileName, file.ContentType, file.Length, file.OpenReadStream(), request.DepartmentId);

        var validation = await _uploadValidator.ValidateAsync(req, ct);
        if (!validation.IsValid)
            return validation.Errors
                             .Select(e => e.ErrorMessage)
                             .ToValidationResult<DocumentResponseDto>();

        var result = await _document.UploadAsync(req, ct);
        return result.ToCreatedResult();
    }

    [HttpGet("{id:guid}/preview")]
    [EnableRateLimiting("read-heavy")]
    [ProducesResponseType(typeof(FileStreamResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
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
    [EnableRateLimiting("reprocess")]
    [ProducesResponseType(typeof(ApiResponse<DocumentResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Reprocess(Guid id, CancellationToken ct)
    {
        var result = await _document.ReprocessAsync(id, ct);
        return result.ToActionResult();
    }

    // Çoklu reprocess — tek HTTP isteği, N belge. Throttle queue tarafında (consumer maxConcurrent).
    // Rate limit batch-delete ile aynı kategori (10/dk) çünkü user-facing operasyon, içerideki
    // pahalı kısım kuyrukta serialize ediliyor.
    [HttpPost("batch-reprocess")]
    [EnableRateLimiting("batch-delete")]
    [ProducesResponseType(typeof(ApiResponse<int>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> ReprocessBatch(
        [FromBody] BatchDocumentReprocessRequestDto req, CancellationToken ct)
    {
        var validation = await _batchReprocessValidator.ValidateAsync(req, ct);
        if (!validation.IsValid)
            return validation.Errors.Select(e => e.ErrorMessage).ToValidationResult<int>();

        var result = await _document.ReprocessBatchAsync(req.Ids, ct);
        return result.ToActionResult();
    }

    // Tekil silme de depo temizliği ve cache geçersizleştirmesi yapar; tek başına ağır olmasa da
    // döngüye sokulduğunda yük çıkardığı için toplu silme ile aynı şekilde sınırlandırılır.
    [HttpDelete("{id:guid}")]
    [EnableRateLimiting("admin-write")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var result = await _document.DeleteAsync(id, ct);
        return result.ToNoContentResult();
    }

    [HttpPost("batch-delete")]
    [EnableRateLimiting("batch-delete")]
    [ProducesResponseType(typeof(ApiResponse<int>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> DeleteBatch(
        [FromBody] BatchDocumentDeleteRequestDto req, CancellationToken ct)
    {
        var validation = await _batchDeleteValidator.ValidateAsync(req, ct);
        if (!validation.IsValid)
            return validation.Errors.Select(e => e.ErrorMessage).ToValidationResult<int>();

        var result = await _document.DeleteBatchAsync(req.Ids, ct);
        return result.ToActionResult();
    }
}
