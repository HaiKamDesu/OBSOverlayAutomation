namespace TournamentAutomation.Application.Commands;

public interface ICommand
{
    bool RecordInHistory { get; }
    string Description { get; }
    Task<CommandResult> ExecuteAsync(CommandContext context, CancellationToken cancellationToken);
    Task<CommandResult> UndoAsync(CommandContext context, CancellationToken cancellationToken);
}
