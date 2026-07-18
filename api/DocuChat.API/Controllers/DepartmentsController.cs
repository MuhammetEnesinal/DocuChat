using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using DocuChat.Application.Common.Results;
using DocuChat.Application.DTOs.Departments;
using DocuChat.Application.Interfaces.Services.Departments;
using DocuChat.API.Common;
using DocuChat.Domain.Enums;

namespace DocuChat.API.Controllers;

[ApiController]
[Route("api/departments")]
[Authorize(Roles = Roles.Admin)]
public class DepartmentsController : ControllerBase
{
    private readonly IDepartmentService _departments;
    private readonly IValidator<CreateDepartmentRequestDto> _createValidator;
    private readonly IValidator<UpdateDepartmentRequestDto> _updateValidator;
    private readonly IValidator<BatchDepartmentDeleteRequestDto> _batchDeleteValidator;

    public DepartmentsController(
        IDepartmentService departments,
        IValidator<CreateDepartmentRequestDto> createValidator,
        IValidator<UpdateDepartmentRequestDto> updateValidator,
        IValidator<BatchDepartmentDeleteRequestDto> batchDeleteValidator)
    {
        _departments = departments;
        _createValidator = createValidator;
        _updateValidator = updateValidator;
        _batchDeleteValidator = batchDeleteValidator;
    }

    // page verilirse SQL-level pagination + arama (yönetim listesi); verilmezse tam liste (seçiciler).
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<DepartmentResponseDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetAll(
        [FromQuery] int? page, [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null, CancellationToken ct = default)
    {
        if (page.HasValue)
        {
            var p = Math.Max(1, page.Value);
            var ps = Math.Clamp(pageSize, 1, 100);
            var paged = await _departments.GetPagedAsync(p, ps, search, ct);
            return paged.ToActionResult();
        }
        var result = await _departments.GetAllAsync(ct);
        return result.ToActionResult();
    }

    [HttpPost]
    [EnableRateLimiting("admin-write")]
    [ProducesResponseType(typeof(ApiResponse<DepartmentResponseDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Create([FromBody] CreateDepartmentRequestDto req, CancellationToken ct)
    {
        var validation = await _createValidator.ValidateAsync(req, ct);
        if (!validation.IsValid)
            return validation.Errors.Select(e => e.ErrorMessage).ToValidationResult<DepartmentResponseDto>();

        var result = await _departments.CreateAsync(req, ct);
        return result.ToCreatedResult();
    }

    [HttpPut("{id:guid}")]
    [EnableRateLimiting("admin-write")]
    [ProducesResponseType(typeof(ApiResponse<DepartmentResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateDepartmentRequestDto req, CancellationToken ct)
    {
        var validation = await _updateValidator.ValidateAsync(req, ct);
        if (!validation.IsValid)
            return validation.Errors.Select(e => e.ErrorMessage).ToValidationResult<DepartmentResponseDto>();

        var result = await _departments.UpdateAsync(id, req, ct);
        return result.ToActionResult();
    }

    [HttpDelete("{id:guid}")]
    [EnableRateLimiting("admin-write")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var result = await _departments.DeleteAsync(id, ct);
        return result.ToNoContentResult();
    }

    // Çoklu silme — tek istek, N departman. Bağlı belge/kullanıcısı olanlar atlanır (hata değil);
    // dönen sayı gerçekten silinen adedidir. Rate limit diğer batch işlemleriyle aynı (10/dk).
    [HttpPost("batch-delete")]
    [EnableRateLimiting("batch-delete")]
    [ProducesResponseType(typeof(ApiResponse<int>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> DeleteBatch(
        [FromBody] BatchDepartmentDeleteRequestDto req, CancellationToken ct)
    {
        var validation = await _batchDeleteValidator.ValidateAsync(req, ct);
        if (!validation.IsValid)
            return validation.Errors.Select(e => e.ErrorMessage).ToValidationResult<int>();

        var result = await _departments.DeleteBatchAsync(req.Ids, ct);
        return result.ToActionResult();
    }
}
