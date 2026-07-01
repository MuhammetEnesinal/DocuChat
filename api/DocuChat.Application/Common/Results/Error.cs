namespace DocuChat.Application.Common.Results;

public class Error
{
    public string Code { get; set; }
    public string Message { get; set; }
    public int StatusCode { get; set; }

    public Error(string Code, string Message, int StatusCode)
    {
        this.Code = Code;
        this.Message = Message;
        this.StatusCode = StatusCode;
    }

    public static Error None => new(string.Empty, string.Empty, 0);
    public static Error NotFound(string msg) => new("NOT_FOUND", msg, 404);
    public static Error Validation(string msg) => new("VALIDATION", msg, 422);
    public static Error Unauthorized(string msg) => new("UNAUTHORIZED", msg, 401);
    public static Error Forbidden(string msg) => new("FORBIDDEN", msg, 403);
    public static Error Conflict(string msg) => new("CONFLICT", msg, 409);
    public static Error Internal(string msg) => new("INTERNAL", msg, 500);
}
