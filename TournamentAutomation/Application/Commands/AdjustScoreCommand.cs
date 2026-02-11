using TournamentAutomation.Domain;

namespace TournamentAutomation.Application.Commands;

public sealed class AdjustScoreCommand : ICommand
{
    private readonly bool _isPlayerOne;
    private readonly int _delta;
    private MatchState? _before;

    public bool RecordInHistory => true;
    public AdjustScoreCommand(bool isPlayerOne, int delta)
    {
        _isPlayerOne = isPlayerOne;
        _delta = delta;
    }

    public string Description => $"Adjust {(_isPlayerOne ? "P1" : "P2")} score by {_delta}";

    public async Task<CommandResult> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        _before = context.State.CurrentMatch;

        var match = context.State.CurrentMatch;
        var min = context.Config.Defaults.ScoreMin;
        var max = match.WinsRequired;

        var player = _isPlayerOne ? match.Player1 : match.Player2;
        var nextScore = Math.Clamp(player.Score + _delta, min, max);
        var updatedPlayer = player.WithScore(nextScore);

        var updatedMatch = _isPlayerOne
            ? match with { Player1 = updatedPlayer }
            : match with { Player2 = updatedPlayer };

        context.State.SetCurrentMatch(updatedMatch);

        if (updatedMatch.IsMatchPointForP1)
            context.Logger.Info("MATCH POINT: P1 is on match point.");
        if (updatedMatch.IsMatchPointForP2)
            context.Logger.Info("MATCH POINT: P2 is on match point.");

        var ok = await context.Overlay.ApplyScoresAsync(updatedMatch, cancellationToken);
        return ok
            ? CommandResult.Success("Score updated.")
            : CommandResult.Fail("Score updated locally but overlay update failed.");
    }

    public async Task<CommandResult> UndoAsync(CommandContext context, CancellationToken cancellationToken)
    {
        if (_before is null)
            return CommandResult.Fail("No previous score snapshot available.");

        context.State.SetCurrentMatch(_before);
        var ok = await context.Overlay.ApplyScoresAsync(_before, cancellationToken);
        return ok
            ? CommandResult.Success("Score restored.")
            : CommandResult.Fail("Score restored locally but overlay update failed.");
    }
}
