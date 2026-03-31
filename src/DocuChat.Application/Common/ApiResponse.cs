namespace DocuChat.Application.Common;

public record ApiError(
    string Code,
    string Message,
    int Status,
    List<string>? Errors = null);   // validation için birden fazla hata

public record ApiResponse<T>(
    bool Success,
    T? Data,
    ApiError? Error)
{
    public static ApiResponse<T> Ok(T data)
        => new(true, data, null);

    public static ApiResponse<T> Fail(ApiError error)
        => new(false, default, error);
}