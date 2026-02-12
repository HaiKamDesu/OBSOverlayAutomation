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
}
