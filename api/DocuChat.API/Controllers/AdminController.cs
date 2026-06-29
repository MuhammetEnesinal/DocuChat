using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using DocuChat.Application.Interfaces.Services;
using DocuChat.Application.DTOs.Auth;
using DocuChat.API.Common;
using DocuChat.API.Extensions;
using DocuChat.Domain.Enums;

namespace DocuChat.API.Controllers;

[ApiController]
[Route("api/admin")]
[Authorize(Roles = Roles.Admin)]
public class AdminController : ControllerBase
{
    private readonly IUserManagementService _userManagement;
    private readonly IValidator<RegisterRequestDto> _registerValidator;
    private readonly IValidator<UpdateUserRequestDto> _updateUserValidator;
    private readonly IValidator<BatchUserDeleteRequestDto> _batchDeleteValidator;

    public AdminController(
        IUserManagementService userManagement,
        IValidator<RegisterRequestDto> registerValidator,
        IValidator<UpdateUserRequestDto> updateUserValidator,
        IValidator<BatchUserDeleteRequestDto> batchDeleteValidator)
    {
        _userManagement = userManagement;
        _registerValidator = registerValidator;
        _updateUserValidator = updateUserValidator;
        _batchDeleteValidator = batchDeleteValidator;
    }

    [HttpGet("users")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<UserSummaryResponseDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetAllUsers(CancellationToken ct)
    {
        var result = await _userManagement.GetAllUsersAsync(ct);
        return result.ToActionResult();
    }

    [HttpPost("users")]
    [EnableRateLimiting("user-write")]
    [ProducesResponseType(typeof(ApiResponse<UserSummaryResponseDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> CreateUser([FromBody] RegisterRequestDto req, CancellationToken ct)
    {
        var validation = await _registerValidator.ValidateAsync(req, ct);
        if (!validation.IsValid)
            return validation.Errors
                             .Select(e => e.ErrorMessage)
                             .ToValidationResult<UserSummaryResponseDto>();

        var result = await _userManagement.CreateUserAsync(req, ct);
        return result.ToCreatedResult();
    }

    [HttpPut("users/{id}")]
    [EnableRateLimiting("user-write")]
    [ProducesResponseType(typeof(ApiResponse<UserSummaryResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> UpdateUser(string id, [FromBody] UpdateUserRequestDto req, CancellationToken ct)
    {
        var validation = await _updateUserValidator.ValidateAsync(req, ct);
        if (!validation.IsValid)
            return validation.Errors
                             .Select(e => e.ErrorMessage)
                             .ToValidationResult<UserSummaryResponseDto>();

        var result = await _userManagement.UpdateUserAsync(id, req, ct);
        return result.ToActionResult();
    }

    [HttpDelete("users/{id}")]
    [EnableRateLimiting("user-write")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> DeleteUser(string id, CancellationToken ct)
    {
        var result = await _userManagement.DeleteUserAsync(id, ct);
        return result.ToNoContentResult();
    }

    // Çoklu kullanıcı silme — tek HTTP isteği. UI N kullanıcı seçtiğinde N tek-tek DELETE
    // göndermek yerine tek batch isteği yollar → rate limit hızla tükenmiyor.
    [HttpPost("users/batch-delete")]
    [EnableRateLimiting("batch-delete")]
    [ProducesResponseType(typeof(ApiResponse<int>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> DeleteUsersBatch(
        [FromBody] BatchUserDeleteRequestDto req, CancellationToken ct)
    {
        var validation = await _batchDeleteValidator.ValidateAsync(req, ct);
        if (!validation.IsValid)
            return validation.Errors.Select(e => e.ErrorMessage).ToValidationResult<int>();

        var result = await _userManagement.DeleteUsersBatchAsync(req.Ids, ct);
        return result.ToActionResult();
    }

    // ── Bulk Import (Excel) ──

    [HttpGet("users/bulk-import/template")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public IActionResult DownloadBulkImportTemplate()
    {
        var bytes = _userManagement.GenerateBulkImportTemplate();
        return File(
            bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "kullanici-toplu-yukleme-sablonu.xlsx");
    }

    // Excel'den toplu kullanıcı import — multipart .xlsx, per-row sonuç raporu döner.
    // user-write rate limit'inde — bulk işlem tek istek olduğu için yeterli.
    [HttpPost("users/bulk-import")]
    [EnableRateLimiting("user-write")]
    [ProducesResponseType(typeof(ApiResponse<BulkImportUsersSummaryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> BulkImportUsers(IFormFile file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return new[] { "Dosya boş veya seçilmedi." }
                .ToValidationResult<BulkImportUsersSummaryDto>();

        var ext = System.IO.Path.GetExtension(file.FileName)?.ToLowerInvariant();
        if (ext != ".xlsx")
            return new[] { "Sadece .xlsx formatı kabul edilir." }
                .ToValidationResult<BulkImportUsersSummaryDto>();

        using var stream = file.OpenReadStream();
        var result = await _userManagement.BulkImportUsersFromExcelAsync(stream, ct);
        return result.ToActionResult();
    }
}
