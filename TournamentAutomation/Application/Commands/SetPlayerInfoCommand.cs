using TournamentAutomation.Domain;

namespace TournamentAutomation.Application.Commands;

public sealed class SetPlayerInfoCommand : ICommand
{
    private readonly bool _isPlayerOne;
    private readonly PlayerInfo _profile;
    private MatchState? _before;

    public SetPlayerInfoCommand(bool isPlayerOne, PlayerInfo profile)
    {
        _isPlayerOne = isPlayerOne;
        _profile = profile;
    }

    public bool RecordInHistory => true;
    public string Description => $"Set {(_isPlayerOne ? "P1" : "P2")} player info";

    public async Task<CommandResult> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        _before = context.State.CurrentMatch;
        var match = context.State.CurrentMatch;

        var currentPlayer = _isPlayerOne ? match.Player1 : match.Player2;
        var updatedPlayer = _profile with { Score = currentPlayer.Score };

        var updatedMatch = _isPlayerOne
            ? match with { Player1 = updatedPlayer }
            : match with { Player2 = updatedPlayer };

        context.State.SetCurrentMatch(updatedMatch);

        var ok = await context.Overlay.ApplyPlayersAsync(updatedMatch, cancellationToken);
        return ok
            ? CommandResult.Success("Player info updated.")
            : CommandResult.Fail("Player info updated locally but overlay update failed.");
    }

    public async Task<CommandResult> UndoAsync(CommandContext context, CancellationToken cancellationToken)
    {
        if (_before is null)
            return CommandResult.Fail("No previous player snapshot available.");

        context.State.SetCurrentMatch(_before);
        var ok = await context.Overlay.ApplyPlayersAsync(_before, cancellationToken);
        return ok
            ? CommandResult.Success("Player info restored.")
            : CommandResult.Fail("Player info restored locally but overlay update failed.");
    }
}
