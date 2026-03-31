using Microsoft.AspNetCore.Mvc;
using DocuChat.Application.Common;

namespace DocuChat.API.Extensions;

public static class ResultExtensions
{
    public static IActionResult ToActionResult<T>(this Result<T> result)
    {
        if (result.IsSuccess)
            return new OkObjectResult(ApiResponse<T>.Ok(result.Value!));

        var apiError = new ApiError(
            result.Error.Code,
            result.Error.Message,
            result.Error.StatusCode);

        return new ObjectResult(ApiResponse<T>.Fail(apiError))
        {
            StatusCode = result.Error.StatusCode
        };
    }

    // Validation hataları için — birden fazla mesaj liste olarak gelir
    public static IActionResult ToValidationResult<T>(
        this IEnumerable<string> errors)
    {
        var apiError = new ApiError(
            Code: "VALIDATION",
            Message: "Doğrulama hatası oluştu.",
            Status: 422,
            Errors: errors.ToList());

        return new ObjectResult(ApiResponse<T>.Fail(apiError))
        {
            StatusCode = 422
        };
    }
}