using ChallongeProfileScraper.Services;

Console.Write("Challonge username: ");
var username = Console.ReadLine();
if (string.IsNullOrWhiteSpace(username))
{
    Console.WriteLine("Username is required.");
    return;
}

try
{
    var scraper = new ChallongeProfileScraperService();
    var stats = await scraper.ScrapeByUsernameAsync(username);

    Console.WriteLine();
    Console.WriteLine($"Username: {stats.Username}");
    Console.WriteLine($"Profile URL: {stats.ProfilePageUrl}");
    Console.WriteLine($"Profile picture URL: {stats.ProfilePictureUrl ?? "(not found)"}");
    Console.WriteLine($"Banner image URL: {stats.BannerImageUrl ?? "(not found)"}");
    Console.WriteLine($"Win rate: {(stats.WinRatePercent.HasValue ? $"{stats.WinRatePercent.Value:0.##}%" : "(not found)")}");
    Console.WriteLine($"Wins: {Format(stats.TotalWins)}");
    Console.WriteLine($"Losses: {Format(stats.TotalLosses)}");
    Console.WriteLine($"Ties: {Format(stats.TotalTies)}");
    Console.WriteLine($"Total tournaments participated: {Format(stats.TotalTournamentsParticipated)}");
    Console.WriteLine($"1st place finishes: {Format(stats.FirstPlaceFinishes)}");
    Console.WriteLine($"2nd place finishes: {Format(stats.SecondPlaceFinishes)}");
    Console.WriteLine($"3rd place finishes: {Format(stats.ThirdPlaceFinishes)}");
    Console.WriteLine($"Last place finishes: {Format(stats.LastPlaceFinishes)}");
    Console.WriteLine($"Top 10 finishes: {Format(stats.TopTenFinishes)}");
}
catch (Exception ex)
{
    Console.WriteLine($"Error: {ex.Message}");
}

static string Format(int? value) => value?.ToString() ?? "(not found)";
