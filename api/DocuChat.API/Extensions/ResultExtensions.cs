using Microsoft.AspNetCore.Mvc;
using DocuChat.Application.Common.Results;
using DocuChat.API.Common;

namespace DocuChat.API.Extensions;

public static class ResultExtensions
{
    public static IActionResult ToActionResult<T>(this Result<T> result)
    {
        if (result.IsSuccess)
            return new OkObjectResult(ApiResponse<T>.Ok(result.Value!));

        return ToErrorResult<T>(result.Error);
    }

    public static IActionResult ToCreatedResult<T>(this Result<T> result)
    {
        if (result.IsSuccess)
            return new ObjectResult(ApiResponse<T>.Ok(result.Value!)) { StatusCode = 201 };

        return ToErrorResult<T>(result.Error);
    }

    public static IActionResult ToNoContentResult(this Result<bool> result)
    {
        if (result.IsSuccess)
            return new NoContentResult();

        return ToErrorResult<bool>(result.Error);
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

    private static IActionResult ToErrorResult<T>(Error error)
    {
        var apiError = new ApiError(error.Code, error.Message, error.StatusCode);
        return new ObjectResult(ApiResponse<T>.Fail(apiError)) { StatusCode = error.StatusCode };
    }
}
