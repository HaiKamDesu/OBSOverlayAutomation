namespace TournamentAutomation.Application.Commands;

public sealed class InlineCommand : ICommand
{
    private readonly Func<CommandContext, CancellationToken, Task<CommandResult>> _execute;
    private readonly Func<CommandContext, CancellationToken, Task<CommandResult>> _undo;

    public InlineCommand(
        string description,
        Func<CommandContext, CancellationToken, Task<CommandResult>> execute,
        Func<CommandContext, CancellationToken, Task<CommandResult>>? undo = null,
        bool recordInHistory = true)
    {
        Description = description;
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _undo = undo ?? ((_, __) => Task.FromResult(CommandResult.Fail("Undo not implemented.")));
        RecordInHistory = recordInHistory;
    }

    public bool RecordInHistory { get; }
    public string Description { get; }

    public Task<CommandResult> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
        => _execute(context, cancellationToken);

    public Task<CommandResult> UndoAsync(CommandContext context, CancellationToken cancellationToken)
        => _undo(context, cancellationToken);
}
