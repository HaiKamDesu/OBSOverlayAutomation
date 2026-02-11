using TournamentAutomation.Domain;

namespace TournamentAutomation.Application.Commands;

public sealed class ResetMatchCommand : ICommand
{
    private MatchState? _before;

    public bool RecordInHistory => true;
    public string Description => "Reset match";

    public async Task<CommandResult> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        _before = context.State.CurrentMatch;

        var match = context.State.CurrentMatch;
        var reset = match with
        {
            Player1 = match.Player1.WithScore(0),
            Player2 = match.Player2.WithScore(0)
        };

        context.State.SetCurrentMatch(reset);

        var ok = await context.Overlay.ApplyScoresAsync(reset, cancellationToken);
        return ok
            ? CommandResult.Success("Match reset.")
            : CommandResult.Fail("Match reset locally but overlay update failed.");
    }

    public async Task<CommandResult> UndoAsync(CommandContext context, CancellationToken cancellationToken)
    {
        if (_before is null)
            return CommandResult.Fail("No previous match snapshot available.");

        context.State.SetCurrentMatch(_before);
        var ok = await context.Overlay.ApplyScoresAsync(_before, cancellationToken);
        return ok
            ? CommandResult.Success("Match restored.")
            : CommandResult.Fail("Match restored locally but overlay update failed.");
    }
}
