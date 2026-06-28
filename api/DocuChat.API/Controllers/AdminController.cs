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

    public AdminController(
        IUserManagementService userManagement,
        IValidator<RegisterRequestDto> registerValidator,
        IValidator<UpdateUserRequestDto> updateUserValidator)
    {
        _userManagement = userManagement;
        _registerValidator = registerValidator;
        _updateUserValidator = updateUserValidator;
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
}
