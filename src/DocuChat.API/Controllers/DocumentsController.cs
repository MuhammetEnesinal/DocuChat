using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using DocuChat.Application.Common;
using DocuChat.Application.Interfaces.Services;
using DocuChat.Application.Interfaces.Repositories;
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

    private readonly IFileStorage _fileStorage;

    public DocumentsController(
        IDocumentService documentService,
        IValidator<UploadDocumentRequest> uploadValidator,
        IFileStorage fileStorage)
    {
        _documentService = documentService;
        _uploadValidator = uploadValidator;
        _fileStorage = fileStorage;
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
        var result = await _documentService.GetFileInfoAsync(id, ct);
        if (!result.IsSuccess) return result.ToActionResult();

        var (storagePath, contentType, fileName) = result.Value;
        try
        {
            var stream = _fileStorage.Read(storagePath);
            Response.Headers["Content-Disposition"] = $"inline; filename=\"{Uri.EscapeDataString(fileName)}\"";
            return File(stream, contentType, enableRangeProcessing: true);
        }
        catch (FileNotFoundException)
        {
            var err = Error.NotFound("Dosya storage'da bulunamadı. Belgeyi yeniden yükleyin.");
            return Result<string>.Failure(err).ToActionResult();
        }
        catch (Exception ex)
        {
            var err = Error.Internal(ex.Message);
            return Result<string>.Failure(err).ToActionResult();
        }
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