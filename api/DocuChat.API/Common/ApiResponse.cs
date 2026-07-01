namespace DocuChat.API.Common;

public class ApiError
{
    public string Code { get; set; }
    public string Message { get; set; }
    public int Status { get; set; }
    public List<string>? Errors { get; set; }   // validation için birden fazla hata

    public ApiError(string Code, string Message, int Status, List<string>? Errors = null)
    {
        this.Code = Code;
        this.Message = Message;
        this.Status = Status;
        this.Errors = Errors;
    }
}

public class ApiResponse<T>
{
    public bool Success { get; set; }
    public T? Data { get; set; }
    public ApiError? Error { get; set; }

    public ApiResponse(bool Success, T? Data, ApiError? Error)
    {
        this.Success = Success;
        this.Data = Data;
        this.Error = Error;
    }

    public static ApiResponse<T> Ok(T data)
        => new(true, data, null);

    public static ApiResponse<T> Fail(ApiError error)
        => new(false, default, error);
}
