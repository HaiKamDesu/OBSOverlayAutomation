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
        ok &= await _obs.SetTextAsync(_mapping.P1Name, match.Player1.Name, cancellationToken);
        ok &= await _obs.SetTextAsync(_mapping.P1Team, match.Player1.Team, cancellationToken);

        var p1Country = _metadata.GetCountry(match.Player1.Country);
        ok &= await _obs.SetTextAsync(_mapping.P1Country, p1Country.Acronym, cancellationToken);
        if (!string.IsNullOrWhiteSpace(p1Country.FlagPath))
            ok &= await _obs.SetImageFileAsync(_mapping.P1Flag, p1Country.FlagPath, cancellationToken);

        ok &= await _obs.SetTextAsync(_mapping.P2Name, match.Player2.Name, cancellationToken);
        ok &= await _obs.SetTextAsync(_mapping.P2Team, match.Player2.Team, cancellationToken);

        var p2Country = _metadata.GetCountry(match.Player2.Country);
        ok &= await _obs.SetTextAsync(_mapping.P2Country, p2Country.Acronym, cancellationToken);
        if (!string.IsNullOrWhiteSpace(p2Country.FlagPath))
            ok &= await _obs.SetImageFileAsync(_mapping.P2Flag, p2Country.FlagPath, cancellationToken);

        if (!ok)
            _logger.Warn("One or more player fields failed to update.");

        return ok;
    }

    public async Task<bool> ApplyScoresAsync(MatchState match, CancellationToken cancellationToken)
    {
        var ok = true;
        ok &= await _obs.SetTextAsync(_mapping.P1Score, match.Player1.Score.ToString(), cancellationToken);
        ok &= await _obs.SetTextAsync(_mapping.P2Score, match.Player2.Score.ToString(), cancellationToken);

        if (!ok)
            _logger.Warn("One or more score fields failed to update.");

        return ok;
    }

    public async Task<bool> ApplyRoundAsync(MatchState match, CancellationToken cancellationToken)
    {
        var ok = true;
        ok &= await _obs.SetTextAsync(_mapping.RoundLabel, match.RoundLabel, cancellationToken);
        ok &= await _obs.SetTextAsync(_mapping.SetType, match.Format.ToString(), cancellationToken);

        if (!ok)
            _logger.Warn("Round label update failed.");

        return ok;
    }
}
