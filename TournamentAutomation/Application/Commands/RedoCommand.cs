namespace TournamentAutomation.Application.Commands;

public sealed class RedoCommand : ICommand
{
    private readonly CommandDispatcher _dispatcher;

    public RedoCommand(CommandDispatcher dispatcher)
    {
        _dispatcher = dispatcher;
    }

    public bool RecordInHistory => false;
    public string Description => "Redo";

    public Task<CommandResult> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
        => _dispatcher.RedoAsync(context, cancellationToken);

    public Task<CommandResult> UndoAsync(CommandContext context, CancellationToken cancellationToken)
        => _dispatcher.UndoAsync(context, cancellationToken);
}
