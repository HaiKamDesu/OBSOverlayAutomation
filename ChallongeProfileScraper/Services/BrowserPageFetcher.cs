using Microsoft.Playwright;

namespace ChallongeProfileScraper.Services;

internal static class BrowserPageFetcher
{
    public static async Task<string> FetchHtmlAsync(Uri url, CancellationToken cancellationToken)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linkedCts.CancelAfter(TimeSpan.FromSeconds(45));
        var effectiveToken = linkedCts.Token;

        using var registration = effectiveToken.Register(() => { });
        try
        {
            using var playwright = await Playwright.CreateAsync();
            await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = true
            });

            var context = await browser.NewContextAsync(new BrowserNewContextOptions
            {
                UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/132.0.0.0 Safari/537.36",
                Locale = "en-US"
            });

            var page = await context.NewPageAsync();
            var response = await page.GotoAsync(url.ToString(), new PageGotoOptions
            {
                WaitUntil = WaitUntilState.DOMContentLoaded,
                Timeout = 30000
            });

            if (response is null)
                throw new HttpRequestException($"Browser navigation returned no HTTP response for {url}.");

            if (response.Status >= 400)
                throw new HttpRequestException($"Browser fetch failed for {url} with HTTP {response.Status} ({response.StatusText}).");

            // Wait briefly for profile sections to hydrate if JS renders portions of the page.
            _ = await page.WaitForSelectorAsync(".profile-banner, .list-wrapper, .stat-card", new PageWaitForSelectorOptions
            {
                Timeout = 5000
            });

            return await page.ContentAsync();
        }
        catch (PlaywrightException ex) when (ex.Message.Contains("Executable doesn't exist", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Playwright browser executable is missing. Run 'playwright install chromium' in this repository, then retry.",
                ex);
        }
    }
}
