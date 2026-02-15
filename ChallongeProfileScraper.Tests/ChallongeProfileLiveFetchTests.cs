using ChallongeProfileScraper.Services;

namespace ChallongeProfileScraper.Tests;

public sealed class ChallongeProfileLiveFetchTests
{
    [Fact]
    public async Task ScrapeByUsernameAsync_HaiKamDesu_ReturnsPublicProfileStats()
    {
        using var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(45)
        };
        var scraper = new ChallongeProfileScraperService(client);

        var stats = await scraper.ScrapeByUsernameAsync("HaiKamDesu");

        Assert.True(string.Equals("HaiKamDesu", stats.Username, StringComparison.OrdinalIgnoreCase));
        Assert.False(string.IsNullOrWhiteSpace(stats.ProfilePictureUrl));
        Assert.False(string.IsNullOrWhiteSpace(stats.BannerImageUrl));
        Assert.NotNull(stats.WinRatePercent);
        Assert.NotNull(stats.TotalWins);
        Assert.NotNull(stats.TotalLosses);
        Assert.NotNull(stats.TotalTournamentsParticipated);
        Assert.NotNull(stats.TopTenFinishes);

        Assert.True(stats.TotalWins >= 0);
        Assert.True(stats.TotalLosses >= 0);
        Assert.True(stats.TotalTournamentsParticipated >= 0);
        Assert.True(stats.WinRatePercent >= 0);
        Assert.True(stats.WinRatePercent <= 100);
    }
}
