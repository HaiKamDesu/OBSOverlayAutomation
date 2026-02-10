namespace ObsInterface;

public sealed record Result<T>
{
    public bool Ok { get; init; }
    public string Code { get; init; } = ResultCodes.ObsError;
    public string Message { get; init; } = string.Empty;
    public T? Value { get; init; }
    public Exception? Exception { get; init; }

    public static Result<T> Success(T? value, string message = "OK") => new()
    {
        Ok = true,
        Code = "OK",
        Message = message,
        Value = value
    };

    public static Result<T> Fail(string code, string message, Exception? exception = null) => new()
    {
        Ok = false,
        Code = code,
        Message = message,
        Exception = exception
    };
}

public static class ResultCodes
{
    public const string NotConnected = nameof(NotConnected);
    public const string NotFound = nameof(NotFound);
    public const string TypeMismatch = nameof(TypeMismatch);
    public const string InvalidArgument = nameof(InvalidArgument);
    public const string ObsError = nameof(ObsError);
    public const string Timeout = nameof(Timeout);
}
