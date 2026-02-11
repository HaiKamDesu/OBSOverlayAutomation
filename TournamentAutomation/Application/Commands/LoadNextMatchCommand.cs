namespace TournamentAutomation.Application.Commands;

public sealed class LoadNextMatchCommand : ICommand
{
    private TournamentAutomation.Domain.MatchState? _before;

    public bool RecordInHistory => true;
    public string Description => "Load next match";

    public async Task<CommandResult> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        if (!context.State.Queue.TryDequeue(out var next) || next is null)
            return CommandResult.Fail("No matches in queue.");

        _before = context.State.CurrentMatch;
        context.State.SetCurrentMatch(next);

        var ok = await context.Overlay.ApplyMatchAsync(next, cancellationToken);
        return ok
            ? CommandResult.Success("Loaded next match.")
            : CommandResult.Fail("Loaded next match locally but overlay update failed.");
    }

    public async Task<CommandResult> UndoAsync(CommandContext context, CancellationToken cancellationToken)
    {
        if (_before is null)
            return CommandResult.Fail("No previous match snapshot available.");

        context.State.SetCurrentMatch(_before);
        var ok = await context.Overlay.ApplyMatchAsync(_before, cancellationToken);
        return ok
            ? CommandResult.Success("Match restored.")
            : CommandResult.Fail("Match restored locally but overlay update failed.");
    }
}
