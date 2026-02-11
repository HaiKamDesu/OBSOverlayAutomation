namespace TournamentAutomation.Application.Commands;

public sealed record CommandHistoryEntry
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.Now;
    public string Description { get; init; } = "";
    public bool Ok { get; init; }
    public string Message { get; init; } = "";
}
