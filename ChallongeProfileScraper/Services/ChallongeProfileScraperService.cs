using System.Net.Http.Headers;
using System.Net;
using ChallongeProfileScraper.Abstractions;
using ChallongeProfileScraper.Models;
using ChallongeProfileScraper.Parsing;

namespace ChallongeProfileScraper.Services;

public sealed class ChallongeProfileScraperService : IChallongeProfileScraperService
{
    private static readonly Uri ChallongeBaseUri = new("https://challonge.com", UriKind.Absolute);
    private readonly HttpClient _httpClient;

    public ChallongeProfileScraperService(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
    }

    public async Task<ChallongeProfileStats> ScrapeByUsernameAsync(string username, CancellationToken cancellationToken = default)
    {
        var normalizedUsername = NormalizeUsername(username);
        var candidates = BuildCandidateProfileUris(normalizedUsername);
        HttpStatusCode? lastStatusCode = null;
        string? html = null;
        Uri? successUri = null;

        foreach (var candidate in candidates)
        {
            using var request = BuildRequest(candidate);
            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                lastStatusCode = response.StatusCode;
                continue;
            }

            html = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!string.IsNullOrWhiteSpace(html))
            {
                successUri = candidate;
                break;
            }
        }

        // Some Challonge edges return 403 to non-browser HTTP clients.
        // Fallback to a real browser fetch for the same URL so JS/cookie gating can complete.
        if (successUri is null && lastStatusCode == HttpStatusCode.Forbidden)
        {
            Exception? browserFallbackException = null;
            foreach (var candidate in candidates)
            {
                try
                {
                    html = await BrowserPageFetcher.FetchHtmlAsync(candidate, cancellationToken);
                    if (!string.IsNullOrWhiteSpace(html))
                    {
                        successUri = candidate;
                        break;
                    }
                }
                catch (Exception ex)
                {
                    browserFallbackException = ex;
                }
            }

            if (successUri is null && browserFallbackException is not null)
            {
                throw new HttpRequestException(
                    $"HTTP client received 403 for '{normalizedUsername}', and browser fallback also failed: {browserFallbackException.Message}",
                    browserFallbackException,
                    HttpStatusCode.Forbidden);
            }
        }

        if (successUri is null || string.IsNullOrWhiteSpace(html))
        {
            if (lastStatusCode.HasValue)
            {
                throw new HttpRequestException(
                    $"Failed to fetch Challonge profile for '{normalizedUsername}'. Last HTTP {(int)lastStatusCode} ({lastStatusCode}).",
                    null,
                    lastStatusCode);
            }

            throw new HttpRequestException($"Failed to fetch Challonge profile for '{normalizedUsername}'.");
        }

        return ChallongeProfileParser.Parse(html, normalizedUsername, successUri);
    }

    private static string NormalizeUsername(string username)
    {
        if (string.IsNullOrWhiteSpace(username))
            throw new ArgumentException("Username is required.", nameof(username));

        return username.Trim().Trim('/').Replace(" ", string.Empty, StringComparison.Ordinal);
    }

    private static IReadOnlyList<Uri> BuildCandidateProfileUris(string normalizedUsername)
    {
        return
        [
            new Uri($"https://challonge.com/users/{normalizedUsername}", UriKind.Absolute),
            new Uri($"https://challonge.com/en/users/{normalizedUsername}", UriKind.Absolute),
            new Uri($"https://www.challonge.com/users/{normalizedUsername}", UriKind.Absolute),
            new Uri($"https://www.challonge.com/en/users/{normalizedUsername}", UriKind.Absolute)
        ];
    }

    private static HttpRequestMessage BuildRequest(Uri profileUri)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, profileUri);
        request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/132.0.0.0 Safari/537.36");
        request.Headers.Accept.ParseAdd("text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
        request.Headers.AcceptLanguage.ParseAdd("en-US,en;q=0.9");
        request.Headers.TryAddWithoutValidation("Cache-Control", "no-cache");
        request.Headers.TryAddWithoutValidation("Pragma", "no-cache");
        request.Headers.Referrer = ChallongeBaseUri;
        return request;
    }
}
