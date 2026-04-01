using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using DocuChat.Application.Abstractions;
using DocuChat.Application.DTOs.Document;
using DocuChat.API.Extensions;

namespace DocuChat.API.Controllers;

public class UploadFileRequest
{
    public IFormFile File { get; set; } = null!;
}

[ApiController]
[Route("api/documents")]
[Authorize]
public class DocumentsController : ControllerBase
{
    private readonly IDocumentService _documentService;
    private readonly IValidator<UploadDocumentRequest> _uploadValidator;

    public DocumentsController(
        IDocumentService documentService,
        IValidator<UploadDocumentRequest> uploadValidator)
    {
        _documentService = documentService;
        _uploadValidator = uploadValidator;
    }

    // Sadece Admin dosya yükleyebilir
    [HttpPost("upload")]
    [Authorize(Roles = "Admin")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> Upload(
        [FromForm] UploadFileRequest request, CancellationToken ct)
    {
        var file = request.File;

        var req = new UploadDocumentRequest(
            file.FileName,
            file.ContentType,
            file.Length,
            file.OpenReadStream());

        var validation = await _uploadValidator.ValidateAsync(req, ct);
        if (!validation.IsValid)
            return validation.Errors
                             .Select(e => e.ErrorMessage)
                             .ToValidationResult<DocumentResponseDto>();

        var result = await _documentService.UploadAsync(req, ct);
        return result.ToActionResult();
    }

    // Tüm kullanıcılar yüklü belgeleri görebilir
    [HttpGet]
    public async Task<IActionResult> GetAllDocuments(CancellationToken ct)
    {
        var result = await _documentService.GetAllDocumentsAsync(ct);
        return result.ToActionResult();
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var result = await _documentService.GetByIdAsync(id, ct);
        return result.ToActionResult();
    }

    // Silme de sadece Admin
    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var result = await _documentService.DeleteAsync(id, ct);
        return result.ToActionResult();
    }
}