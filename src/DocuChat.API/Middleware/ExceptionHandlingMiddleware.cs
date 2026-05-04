// DocuChat.API/Middleware/ExceptionHandlingMiddleware.cs
using DocuChat.Application.Common;

namespace DocuChat.API.Middleware;

public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(
        RequestDelegate next,
        ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext ctx)
    {
        try { await _next(ctx); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception: {Message}", ex.Message);

            var apiError = new ApiError("INTERNAL_SERVER_ERROR", "Beklenmeyen bir hata oluştu.", 500);
            ctx.Response.ContentType = "application/json";
            ctx.Response.StatusCode = 500;
            await ctx.Response.WriteAsJsonAsync(ApiResponse<object>.Fail(apiError));
        }
    }
}