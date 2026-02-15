using ChallongeProfileScraper.Parsing;

namespace ChallongeProfileScraper.Tests;

public sealed class ChallongeProfileParserTests
{
    [Fact]
    public void Parse_ExtractsExpectedStats_FromSavedProfileFixture()
    {
        var fixturePath = Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "HaiKamDesu's Profile - Challonge.html");
        var html = File.ReadAllText(fixturePath);

        var stats = ChallongeProfileParser.Parse(
            html,
            "haikamdesu",
            new Uri("https://challonge.com/users/haikamdesu"));

        Assert.Equal("haikamdesu", stats.Username);
        Assert.Equal("https://user-assets.challonge.com/users/images/007/553/293/hdpi/image_2025-09-05_211405369.png", stats.ProfilePictureUrl);
        Assert.Equal("https://user-assets.challonge.com/users/banners/007/553/293/original/image_2025-09-05_211312503.png", stats.BannerImageUrl);

        Assert.Equal(50m, stats.WinRatePercent);
        Assert.Equal(31, stats.TotalWins);
        Assert.Equal(31, stats.TotalLosses);
        Assert.Equal(0, stats.TotalTies);

        Assert.Equal(15, stats.TotalTournamentsParticipated);
        Assert.Equal(0, stats.FirstPlaceFinishes);
        Assert.Equal(0, stats.SecondPlaceFinishes);
        Assert.Equal(0, stats.ThirdPlaceFinishes);
        Assert.Null(stats.LastPlaceFinishes);
        Assert.Equal(9, stats.TopTenFinishes);
    }
}
