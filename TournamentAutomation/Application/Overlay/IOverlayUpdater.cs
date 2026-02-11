using TournamentAutomation.Domain;

namespace TournamentAutomation.Application.Overlay;

public interface IOverlayUpdater
{
    Task<bool> ApplyMatchAsync(MatchState match, CancellationToken cancellationToken);
    Task<bool> ApplyPlayersAsync(MatchState match, CancellationToken cancellationToken);
    Task<bool> ApplyScoresAsync(MatchState match, CancellationToken cancellationToken);
    Task<bool> ApplyRoundAsync(MatchState match, CancellationToken cancellationToken);
}
