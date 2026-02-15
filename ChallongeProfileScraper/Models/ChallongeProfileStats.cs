namespace ChallongeProfileScraper.Models;

public sealed record ChallongeProfileStats
{
    public string Username { get; init; } = string.Empty;
    public Uri ProfilePageUrl { get; init; } = new("https://challonge.com", UriKind.Absolute);
    public DateTimeOffset RetrievedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    public string? ProfilePictureUrl { get; init; }
    public string? BannerImageUrl { get; init; }

    public decimal? WinRatePercent { get; init; }
    public int? TotalWins { get; init; }
    public int? TotalLosses { get; init; }
    public int? TotalTies { get; init; }

    public int? TotalTournamentsParticipated { get; init; }
    public int? FirstPlaceFinishes { get; init; }
    public int? SecondPlaceFinishes { get; init; }
    public int? ThirdPlaceFinishes { get; init; }
    public int? LastPlaceFinishes { get; init; }
    public int? TopTenFinishes { get; init; }
}
