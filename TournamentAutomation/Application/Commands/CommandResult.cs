namespace TournamentAutomation.Application.Commands;

public sealed record CommandResult
{
    public bool Ok { get; init; }
    public string Message { get; init; } = "";
    public Exception? Exception { get; init; }

    public static CommandResult Success(string message = "OK") => new() { Ok = true, Message = message };
    public static CommandResult Fail(string message, Exception? exception = null)
        => new() { Ok = false, Message = message, Exception = exception };
}
