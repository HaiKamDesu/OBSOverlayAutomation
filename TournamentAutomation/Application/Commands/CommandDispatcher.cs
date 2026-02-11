using TournamentAutomation.Application.Logging;

namespace TournamentAutomation.Application.Commands;

public sealed class CommandDispatcher
{
    private readonly Stack<ICommand> _undo = new();
    private readonly Stack<ICommand> _redo = new();
    private readonly IAppLogger _logger;

    public CommandDispatcher(IAppLogger logger)
    {
        _logger = logger;
    }

    public IReadOnlyCollection<ICommand> UndoStack => _undo.ToArray();
    public IReadOnlyCollection<ICommand> RedoStack => _redo.ToArray();

    public async Task<CommandResult> ExecuteAsync(ICommand command, CommandContext context, CancellationToken cancellationToken)
    {
        var result = await command.ExecuteAsync(context, cancellationToken);
        Log(command, result);

        if (result.Ok && command.RecordInHistory)
        {
            _undo.Push(command);
            _redo.Clear();
        }

        return result;
    }

    public async Task<CommandResult> UndoAsync(CommandContext context, CancellationToken cancellationToken)
    {
        if (_undo.Count == 0)
            return CommandResult.Fail("Nothing to undo.");

        var command = _undo.Pop();
        var result = await command.UndoAsync(context, cancellationToken);
        Log(command, result, isUndo: true);

        if (result.Ok)
        {
            _redo.Push(command);
        }
        else
        {
            _undo.Push(command);
        }

        return result;
    }

    public async Task<CommandResult> RedoAsync(CommandContext context, CancellationToken cancellationToken)
    {
        if (_redo.Count == 0)
            return CommandResult.Fail("Nothing to redo.");

        var command = _redo.Pop();
        var result = await command.ExecuteAsync(context, cancellationToken);
        Log(command, result, isRedo: true);

        if (result.Ok)
        {
            _undo.Push(command);
        }
        else
        {
            _redo.Push(command);
        }

        return result;
    }

    private void Log(ICommand command, CommandResult result, bool isUndo = false, bool isRedo = false)
    {
        var prefix = isUndo ? "UNDO" : isRedo ? "REDO" : "DO";
        if (result.Ok)
            _logger.Info($"CMD {prefix}: {command.Description} -> {result.Message}");
        else
            _logger.Warn($"CMD {prefix} FAILED: {command.Description} -> {result.Message}");
    }
}
