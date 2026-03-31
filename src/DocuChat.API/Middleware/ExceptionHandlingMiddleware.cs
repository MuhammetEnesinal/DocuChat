using DocuChat.Application.Common;
using DocuChat.Domain.Exceptions;
using FluentValidation;

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
            await HandleAsync(ctx, ex);
        }
    }

    private static Task HandleAsync(HttpContext ctx, Exception ex)
    {
        var (status, code, message, errors) = ex switch
        {
            DomainException d =>
                (d.Code == "DOCUMENT_NOT_FOUND" || d.Code == "SESSION_NOT_FOUND"
                    ? 404 : 400,
                 d.Code, d.Message, (List<string>?)null),

            UnauthorizedAccessException =>
                (401, "UNAUTHORIZED", ex.Message, null),

            ValidationException v =>
                (422, "VALIDATION_ERROR", "Doğrulama hatası oluştu.",
                 v.Errors.Select(e => e.ErrorMessage).ToList()),

            KeyNotFoundException =>
                (404, "NOT_FOUND", ex.Message, null),

            _ =>
                (500, "INTERNAL_SERVER_ERROR", "Beklenmeyen bir hata oluştu.", null)
        };

        var apiError = new ApiError(code, message, status, errors);

        ctx.Response.ContentType = "application/json";
        ctx.Response.StatusCode = status;

        return ctx.Response.WriteAsJsonAsync(
            ApiResponse<object>.Fail(apiError));
    }
}