namespace TournamentAutomation.Application.Commands;

public sealed class UndoCommand : ICommand
{
    private readonly CommandDispatcher _dispatcher;

    public UndoCommand(CommandDispatcher dispatcher)
    {
        _dispatcher = dispatcher;
    }

    public bool RecordInHistory => false;
    public string Description => "Undo";

    public Task<CommandResult> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
        => _dispatcher.UndoAsync(context, cancellationToken);

    public Task<CommandResult> UndoAsync(CommandContext context, CancellationToken cancellationToken)
        => _dispatcher.RedoAsync(context, cancellationToken);
}
