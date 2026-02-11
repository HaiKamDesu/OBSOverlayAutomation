using TournamentAutomation.Configuration;

namespace TournamentAutomation.Application.Commands;

public sealed class SwitchSceneCommand : ICommand
{
    private readonly string _sceneName;
    private string? _previousScene;

    public bool RecordInHistory => true;
    public SwitchSceneCommand(string sceneName)
    {
        _sceneName = sceneName;
    }

    public string Description => $"Switch scene to '{_sceneName}'";

    public async Task<CommandResult> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        _previousScene = context.State.CurrentScene;
        context.State.CurrentScene = _sceneName;

        var ok = await context.Obs.SwitchSceneAsync(_sceneName, cancellationToken);
        return ok
            ? CommandResult.Success($"Scene set to '{_sceneName}'.")
            : CommandResult.Fail($"Failed to switch to '{_sceneName}'.");
    }

    public async Task<CommandResult> UndoAsync(CommandContext context, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_previousScene))
            return CommandResult.Fail("Previous scene not available.");

        context.State.CurrentScene = _previousScene;
        var ok = await context.Obs.SwitchSceneAsync(_previousScene, cancellationToken);
        return ok
            ? CommandResult.Success($"Scene restored to '{_previousScene}'.")
            : CommandResult.Fail($"Failed to restore scene '{_previousScene}'.");
    }
}
