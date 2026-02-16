using TournamentAutomation.Application.Logging;
using TournamentAutomation.Configuration;
using TournamentAutomation.Domain;
using TournamentAutomation.Infrastructure;

namespace TournamentAutomation.Application.Overlay;

public sealed class OverlayUpdater : IOverlayUpdater
{
    private readonly IObsGateway _obs;
    private readonly OverlayMapping _mapping;
    private readonly OverlayMetadata _metadata;
    private readonly IAppLogger _logger;

    public OverlayUpdater(IObsGateway obs, OverlayMapping mapping, OverlayMetadata metadata, IAppLogger logger)
    {
        _obs = obs;
        _mapping = mapping;
        _metadata = metadata;
        _logger = logger;
    }

    public async Task<bool> ApplyMatchAsync(MatchState match, CancellationToken cancellationToken)
    {
        var ok = true;
        ok &= await ApplyRoundAsync(match, cancellationToken);
        ok &= await ApplyPlayersAsync(match, cancellationToken);
        ok &= await ApplyScoresAsync(match, cancellationToken);
        return ok;
    }

    public async Task<bool> ApplyPlayersAsync(MatchState match, CancellationToken cancellationToken)
    {
        var ok = true;
        ok &= await SetTextIfMappedAsync(_mapping.P1Name, match.Player1.Name, cancellationToken);
        ok &= await SetTextIfMappedAsync(_mapping.P1Team, match.Player1.Team, cancellationToken);

        var p1Country = _metadata.GetCountry(match.Player1.Country);
        var p1CountryCode = string.IsNullOrWhiteSpace(match.Player1.CustomCountryCode) ? p1Country.Acronym : match.Player1.CustomCountryCode;
        var p1FlagPath = string.IsNullOrWhiteSpace(match.Player1.CustomFlagPath) ? p1Country.FlagPath : match.Player1.CustomFlagPath;
        ok &= await SetTextIfMappedAsync(_mapping.P1Country, p1CountryCode, cancellationToken);
        if (!string.IsNullOrWhiteSpace(p1FlagPath))
            ok &= await SetImageIfMappedAsync(_mapping.P1Flag, p1FlagPath, cancellationToken);

        ok &= await SetTextIfMappedAsync(_mapping.P2Name, match.Player2.Name, cancellationToken);
        ok &= await SetTextIfMappedAsync(_mapping.P2Team, match.Player2.Team, cancellationToken);

        var p2Country = _metadata.GetCountry(match.Player2.Country);
        var p2CountryCode = string.IsNullOrWhiteSpace(match.Player2.CustomCountryCode) ? p2Country.Acronym : match.Player2.CustomCountryCode;
        var p2FlagPath = string.IsNullOrWhiteSpace(match.Player2.CustomFlagPath) ? p2Country.FlagPath : match.Player2.CustomFlagPath;
        ok &= await SetTextIfMappedAsync(_mapping.P2Country, p2CountryCode, cancellationToken);
        if (!string.IsNullOrWhiteSpace(p2FlagPath))
            ok &= await SetImageIfMappedAsync(_mapping.P2Flag, p2FlagPath, cancellationToken);

        ok &= await ApplyChallongeProfileAsync(
            match.Player1,
            _mapping.P1ChallongeProfileImage,
            _mapping.P1ChallongeBannerImage,
            _mapping.P1ChallongeStatsText,
            cancellationToken);
        ok &= await ApplyChallongeProfileAsync(
            match.Player2,
            _mapping.P2ChallongeProfileImage,
            _mapping.P2ChallongeBannerImage,
            _mapping.P2ChallongeStatsText,
            cancellationToken);
        ok &= await ApplyCharacterSpriteAsync(match.Player1, _mapping.P1CharacterSprite, cancellationToken);
        ok &= await ApplyCharacterSpriteAsync(match.Player2, _mapping.P2CharacterSprite, cancellationToken);

        if (!ok)
            _logger.Warn("One or more player fields failed to update.");

        return ok;
    }

    public async Task<bool> ApplyScoresAsync(MatchState match, CancellationToken cancellationToken)
    {
        var ok = true;
        ok &= await SetTextIfMappedAsync(_mapping.P1Score, match.Player1.Score.ToString(), cancellationToken);
        ok &= await SetTextIfMappedAsync(_mapping.P2Score, match.Player2.Score.ToString(), cancellationToken);

        if (!ok)
            _logger.Warn("One or more score fields failed to update.");

        return ok;
    }

    public async Task<bool> ApplyRoundAsync(MatchState match, CancellationToken cancellationToken)
    {
        var ok = true;
        ok &= await SetTextIfMappedAsync(_mapping.RoundLabel, match.RoundLabel, cancellationToken);
        ok &= await SetTextIfMappedAsync(_mapping.SetType, match.Format.ToString(), cancellationToken);

        if (!ok)
            _logger.Warn("Round label update failed.");

        return ok;
    }

    private async Task<bool> SetTextIfMappedAsync(string inputName, string value, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(inputName))
            return true;

        return await _obs.SetTextAsync(inputName, value, cancellationToken);
    }

    private async Task<bool> SetImageIfMappedAsync(string inputName, string filePath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(inputName))
            return true;

        return await _obs.SetImageFileAsync(inputName, filePath, cancellationToken);
    }

    private async Task<bool> ApplyChallongeProfileAsync(
        PlayerInfo player,
        string profileImageInput,
        string bannerImageInput,
        string statsTextInput,
        CancellationToken cancellationToken)
    {
        var profileImageSource = ResolvePreferredMediaSource(
            player.ChallongeProfile?.ProfilePictureUrl,
            _mapping.ChallongeDefaultProfileImagePath);
        var bannerImageSource = ResolvePreferredMediaSource(
            player.ChallongeProfile?.BannerImageUrl,
            _mapping.ChallongeDefaultBannerImagePath);
        var statsText = BuildStatsText(player);

        var ok = true;
        ok &= await SetMediaIfMappedAsync(profileImageInput, profileImageSource, cancellationToken);
        ok &= await SetMediaIfMappedAsync(bannerImageInput, bannerImageSource, cancellationToken);
        ok &= await SetTextIfMappedAsync(statsTextInput, statsText, cancellationToken);
        return ok;
    }

    private async Task<bool> ApplyCharacterSpriteAsync(
        PlayerInfo player,
        string characterSpriteInput,
        CancellationToken cancellationToken)
    {
        var firstCharacter = ResolvePrimaryCharacterName(player);
        var spritePath = _metadata.ResolveCharacterSpritePath(firstCharacter);
        return await SetMediaIfMappedAsync(characterSpriteInput, spritePath, cancellationToken);
    }

    private async Task<bool> SetMediaIfMappedAsync(string inputName, string source, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(inputName))
            return true;

        return await _obs.SetMediaSourceAsync(inputName, source, cancellationToken);
    }

    private string BuildStatsText(PlayerInfo player)
    {
        var profile = player.ChallongeProfile;
        if (profile is null)
            return _mapping.ChallongeDefaultStatsText ?? string.Empty;

        var template = string.IsNullOrWhiteSpace(_mapping.ChallongeStatsTemplate)
            ? "W-L {wins}-{losses} | WR {win_rate}%"
            : _mapping.ChallongeStatsTemplate;

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["username"] = !string.IsNullOrWhiteSpace(profile.Username) ? profile.Username : player.ChallongeUsername,
            ["wins"] = FormatStat(profile.TotalWins),
            ["losses"] = FormatStat(profile.TotalLosses),
            ["ties"] = FormatStat(profile.TotalTies),
            ["win_rate"] = profile.WinRatePercent.HasValue ? profile.WinRatePercent.Value.ToString("0.#") : "-",
            ["tournaments"] = FormatStat(profile.TotalTournamentsParticipated),
            ["first"] = FormatStat(profile.FirstPlaceFinishes),
            ["second"] = FormatStat(profile.SecondPlaceFinishes),
            ["third"] = FormatStat(profile.ThirdPlaceFinishes),
            ["top10"] = FormatStat(profile.TopTenFinishes),
            ["characters"] = BuildCharactersText(player),
            ["profile_url"] = profile.ProfilePageUrl ?? string.Empty,
            ["retrieved_at"] = profile.RetrievedAtUtc.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss")
        };

        var text = template;
        foreach (var pair in values)
            text = text.Replace($"{{{pair.Key}}}", pair.Value, StringComparison.OrdinalIgnoreCase);

        return text.Replace("\\n", "\n", StringComparison.Ordinal);
    }

    private static string ResolvePreferredMediaSource(string? source, string? fallback)
    {
        var primary = source?.Trim() ?? string.Empty;
        var secondary = fallback?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(primary) && string.IsNullOrWhiteSpace(secondary))
            return string.Empty;

        if (!string.IsNullOrWhiteSpace(primary))
            return primary;

        return secondary;
    }

    private static string FormatStat(int? value) => value?.ToString() ?? "-";

    private static string BuildCharactersText(PlayerInfo player)
    {
        if (!string.IsNullOrWhiteSpace(player.Character))
            return player.Character;

        if (player.Characters is { Count: > 0 })
            return string.Join(", ", player.Characters);

        return "-";
    }

    private static string ResolvePrimaryCharacterName(PlayerInfo player)
    {
        if (!string.IsNullOrWhiteSpace(player.Character))
        {
            var first = player.Character
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(first))
                return first;
        }

        if (player.Characters is { Count: > 0 })
            return player.Characters[0].ToString();

        return string.Empty;
    }
}
