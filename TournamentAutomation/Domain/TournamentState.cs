namespace TournamentAutomation.Domain;

public sealed class TournamentState
{
    public MatchState CurrentMatch { get; private set; }
    public MatchQueue Queue { get; } = new();
    public string CurrentScene { get; set; } = string.Empty;

    public TournamentState(MatchState initialMatch)
    {
        CurrentMatch = initialMatch;
    }

    public void SetCurrentMatch(MatchState match)
    {
        ArgumentNullException.ThrowIfNull(match);
        CurrentMatch = match;
    }
}
