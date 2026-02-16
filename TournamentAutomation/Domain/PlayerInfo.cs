namespace TournamentAutomation.Domain;

public sealed record ChallongeProfileInfo
{
    public string Username { get; init; } = "";
    public string ProfilePageUrl { get; init; } = "";
    public string ProfilePictureUrl { get; init; } = "";
    public string BannerImageUrl { get; init; } = "";
    public DateTimeOffset RetrievedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public decimal? WinRatePercent { get; init; }
    public int? TotalWins { get; init; }
    public int? TotalLosses { get; init; }
    public int? TotalTies { get; init; }
    public int? TotalTournamentsParticipated { get; init; }
    public int? FirstPlaceFinishes { get; init; }
    public int? SecondPlaceFinishes { get; init; }
    public int? ThirdPlaceFinishes { get; init; }
    public int? TopTenFinishes { get; init; }
}

public sealed record PlayerInfo
{
    public string Name { get; init; } = "";
    public string Team { get; init; } = "";
    public CountryId Country { get; init; } = CountryId.Unknown;
    public string CustomCountryCode { get; init; } = "";
    public string CustomCountryName { get; init; } = "";
    public string CustomFlagPath { get; init; } = "";
    public IReadOnlyList<FGCharacterId> Characters { get; init; } = Array.Empty<FGCharacterId>();
    public string Character { get; init; } = "";
    public int Score { get; init; }
    public string ChallongeUsername { get; init; } = "";
    public ChallongeProfileInfo? ChallongeProfile { get; init; }

    public PlayerInfo WithScore(int score) => this with { Score = score };
}
