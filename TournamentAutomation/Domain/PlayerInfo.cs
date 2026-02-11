namespace TournamentAutomation.Domain;

public sealed record PlayerInfo
{
    public string Name { get; init; } = "";
    public string Team { get; init; } = "";
    public CountryId Country { get; init; } = CountryId.Unknown;
    public IReadOnlyList<FGCharacterId> Characters { get; init; } = Array.Empty<FGCharacterId>();
    public string Character { get; init; } = "";
    public int Score { get; init; }

    public PlayerInfo WithScore(int score) => this with { Score = score };
}
