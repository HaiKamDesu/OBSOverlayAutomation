namespace TournamentAutomation.Domain;

public sealed record MatchState
{
    public string RoundLabel { get; init; } = "";
    public MatchSetFormat Format { get; init; } = MatchSetFormat.BO5;
    public PlayerInfo Player1 { get; init; } = new();
    public PlayerInfo Player2 { get; init; } = new();

    public int WinsRequired => Format.WinsRequired();

    public bool IsMatchOver => Player1.Score >= WinsRequired || Player2.Score >= WinsRequired;

    public bool IsMatchPointForP1 => Player1.Score == WinsRequired - 1 && Player2.Score < WinsRequired;
    public bool IsMatchPointForP2 => Player2.Score == WinsRequired - 1 && Player1.Score < WinsRequired;
}
