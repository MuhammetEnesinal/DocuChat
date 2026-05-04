// DocuChat.API/Controllers/DocumentsController.cs
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using DocuChat.Application.Common;
using DocuChat.Application.Interfaces.Services;
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
    private readonly IDocumentService _documentService;
    private readonly IValidator<UploadDocumentRequest> _uploadValidator;
    // IFileStorage tamamen kaldırıldı — servis katmanı üzerinden erişiliyor

    public DocumentsController(
        IDocumentService documentService,
        IValidator<UploadDocumentRequest> uploadValidator)
    {
        _documentService = documentService;
        _uploadValidator = uploadValidator;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var result = await _documentService.GetAllDocumentsAsync(ct);
        return result.ToActionResult();
    }

    [HttpGet("{id:guid}/chunks")]
    public async Task<IActionResult> GetChunks(Guid id, CancellationToken ct)
    {
        var result = await _documentService.GetChunksAsync(id, ct);
        return result.ToActionResult();
    }

    [HttpPost("upload")]
    [Consumes("multipart/form-data")]
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

        var result = await _documentService.UploadAsync(req, ct);
        return result.ToActionResult();
    }

    [HttpGet("{id:guid}/preview")]
    public async Task<IActionResult> Preview(Guid id, CancellationToken ct)
    {
        // IFileStorage artık controller'da yok — servis stream'i açıyor
        var result = await _documentService.GetFileStreamAsync(id, ct);
        if (!result.IsSuccess)
            return result.ToActionResult();

        var (stream, contentType, fileName) = result.Value;
        Response.Headers["Content-Disposition"] =
            $"inline; filename=\"{Uri.EscapeDataString(fileName)}\"";

        return File(stream, contentType, enableRangeProcessing: true);
    }

    [HttpPost("{id:guid}/reprocess")]
    public async Task<IActionResult> Reprocess(Guid id, CancellationToken ct)
    {
        var result = await _documentService.ReprocessAsync(id, ct);
        return result.ToActionResult();
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var result = await _documentService.DeleteAsync(id, ct);
        return result.ToActionResult();
    }
}