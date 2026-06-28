// DocuChat.API/Middleware/ExceptionHandlingMiddleware.cs
using DocuChat.API.Common;

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
        catch (OperationCanceledException) when (ctx.RequestAborted.IsCancellationRequested)
        {
            // Kullanıcı sayfa/sohbet değiştirdi → tarayıcı isteği iptal etti. Hata değil.
            _logger.LogInformation("[Request] İstemci isteği iptal etti: {Path}", ctx.Request.Path);
            if (!ctx.Response.HasStarted)
                ctx.Response.StatusCode = 499;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception: {Message}", ex.Message);

            if (ctx.Response.HasStarted)
            {
                // Yanıt zaten başlamış — status/gövde yazılamaz, sadece logla.
                throw;
            }

            var apiError = new ApiError("INTERNAL_SERVER_ERROR", "Beklenmeyen bir hata oluştu.", 500);
            ctx.Response.ContentType = "application/json";
            ctx.Response.StatusCode = 500;
            await ctx.Response.WriteAsJsonAsync(ApiResponse<object>.Fail(apiError));
        }
    }
}
