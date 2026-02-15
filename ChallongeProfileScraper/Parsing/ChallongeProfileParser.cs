using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using ChallongeProfileScraper.Models;

namespace ChallongeProfileScraper.Parsing;

public static class ChallongeProfileParser
{
    public static ChallongeProfileStats Parse(string html, string username, Uri profilePageUrl)
    {
        ArgumentNullException.ThrowIfNull(html);
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentNullException.ThrowIfNull(profilePageUrl);

        var decoded = WebUtility.HtmlDecode(html);
        var winRateSection = ExtractSectionAroundHeading(decoded, "Overall Win Rate", 7000);

        return new ChallongeProfileStats
        {
            Username = username.Trim(),
            ProfilePageUrl = profilePageUrl,
            RetrievedAtUtc = DateTimeOffset.UtcNow,
            ProfilePictureUrl = ExtractDataDefaultImage(decoded, "logo_url"),
            BannerImageUrl = ExtractDataDefaultImage(decoded, "banner_url"),
            WinRatePercent = ParsePercentage(ExtractDataValueByLabel(winRateSection, "Win rate")),
            TotalWins = ParseInt(ExtractDataValueByLabel(winRateSection, "Wins")),
            TotalLosses = ParseInt(ExtractDataValueByLabel(winRateSection, "Losses")),
            TotalTies = ParseInt(ExtractDataValueByLabel(winRateSection, "Ties")),
            TotalTournamentsParticipated = ParseInt(ExtractDataValueByLabel(decoded, "Total tournaments participated")),
            FirstPlaceFinishes = ParseInt(ExtractDataValueByLabel(decoded, "Tournaments won")),
            SecondPlaceFinishes = ParseInt(ExtractDataValueByLabel(decoded, "Finished 2nd")),
            ThirdPlaceFinishes = ParseInt(ExtractDataValueByLabel(decoded, "Finished 3rd")),
            LastPlaceFinishes = ParseInt(
                ExtractDataValueByLabel(decoded, "Finished last")
                ?? ExtractDataValueByLabel(decoded, "Finished in last place")
                ?? ExtractDataValueByLabel(decoded, "Last place finishes")),
            TopTenFinishes = ParseInt(ExtractDataValueByLabel(decoded, "Finished in top 10"))
        };
    }

    private static string? ExtractDataDefaultImage(string html, string expectation)
    {
        var pattern =
            @"<div\b(?=[^>]*\brole\s*=\s*[""']react-inline-image[""'])(?=[^>]*\bdata-expectation\s*=\s*[""']" +
            Regex.Escape(expectation) +
            @"[""'])(?=[^>]*\bdata-default-image\s*=\s*[""'](?<url>[^""']+)[""'])[^>]*>";

        var match = Regex.Match(html, pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline);
        return match.Success ? NormalizeUrl(match.Groups["url"].Value) : null;
    }

    private static string? ExtractDataValueByLabel(string html, string label)
    {
        if (string.IsNullOrWhiteSpace(html))
            return null;

        var pattern =
            @"<h[34]\b[^>]*\bclass\s*=\s*[""'][^""']*\bdata\b[^""']*[""'][^>]*>\s*(?<value>[^<]+?)\s*</h[34]>\s*" +
            @"<p\b[^>]*\bclass\s*=\s*[""'][^""']*\blbl\b[^""']*[""'][^>]*>\s*" +
            Regex.Escape(label) +
            @"\s*</p>";

        var match = Regex.Match(html, pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline);
        return match.Success ? match.Groups["value"].Value.Trim() : null;
    }

    private static string ExtractSectionAroundHeading(string html, string heading, int maxLength)
    {
        var index = html.IndexOf(heading, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
            return html;

        var length = Math.Min(maxLength, html.Length - index);
        return html.Substring(index, length);
    }

    private static int? ParseInt(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var digits = new string(raw.Where(char.IsDigit).ToArray());
        if (string.IsNullOrWhiteSpace(digits))
            return null;

        return int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    private static decimal? ParsePercentage(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var cleaned = raw.Replace("%", string.Empty, StringComparison.Ordinal).Trim();
        return decimal.TryParse(cleaned, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    private static string NormalizeUrl(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.StartsWith("//", StringComparison.Ordinal))
            return $"https:{trimmed}";

        return trimmed;
    }
}
