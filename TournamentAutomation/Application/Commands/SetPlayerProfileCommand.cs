using TournamentAutomation.Domain;

namespace TournamentAutomation.Application.Commands;

public sealed class SetPlayerProfileCommand : ICommand
{
    private readonly bool _isPlayerOne;
    private readonly string _profileId;
    private MatchState? _before;

    public SetPlayerProfileCommand(bool isPlayerOne, string profileId)
    {
        _isPlayerOne = isPlayerOne;
        _profileId = profileId;
    }

    public bool RecordInHistory => true;
    public string Description => $"Set {(_isPlayerOne ? "P1" : "P2")} profile '{_profileId}'";

    public async Task<CommandResult> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        if (!context.Config.PlayerProfiles.TryGetValue(_profileId, out var profile))
            return CommandResult.Fail($"Profile '{_profileId}' not found.");

        _before = context.State.CurrentMatch;
        var match = context.State.CurrentMatch;

        var currentPlayer = _isPlayerOne ? match.Player1 : match.Player2;
        var updatedPlayer = profile with { Score = currentPlayer.Score };

        var updatedMatch = _isPlayerOne
            ? match with { Player1 = updatedPlayer }
            : match with { Player2 = updatedPlayer };

        context.State.SetCurrentMatch(updatedMatch);

        var ok = await context.Overlay.ApplyPlayersAsync(updatedMatch, cancellationToken);
        return ok
            ? CommandResult.Success("Player profile applied.")
            : CommandResult.Fail("Player profile applied locally but overlay update failed.");
    }

    public async Task<CommandResult> UndoAsync(CommandContext context, CancellationToken cancellationToken)
    {
        if (_before is null)
            return CommandResult.Fail("No previous player snapshot available.");

        context.State.SetCurrentMatch(_before);
        var ok = await context.Overlay.ApplyPlayersAsync(_before, cancellationToken);
        return ok
            ? CommandResult.Success("Player profile restored.")
            : CommandResult.Fail("Player profile restored locally but overlay update failed.");
    }
}
