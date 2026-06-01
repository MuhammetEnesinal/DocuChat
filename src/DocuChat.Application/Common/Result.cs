namespace DocuChat.Application.Common;

public class Result<T>
{
    public bool IsSuccess { get; }
    public T? Value { get; }
    public Error Error { get; }

    private Result(bool ok, T? val, Error err)
    {
        IsSuccess = ok;
        Value = val;
        Error = err;
    }

    public static Result<T> Success(T value) => new(true, value, Error.None);
    public static Result<T> Failure(Error err) => new(false, default, err);
}
