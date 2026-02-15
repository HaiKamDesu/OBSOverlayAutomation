using ChallongeProfileScraper.Models;

namespace ChallongeProfileScraper.Abstractions;

public interface IChallongeProfileScraperService
{
    Task<ChallongeProfileStats> ScrapeByUsernameAsync(string username, CancellationToken cancellationToken = default);
}
