namespace TournamentAutomation.Application.Commands;

public sealed class SwapPlayersCommand : ICommand
{
    private TournamentAutomation.Domain.MatchState? _before;

    public bool RecordInHistory => true;
    public string Description => "Swap players";

    public async Task<CommandResult> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        _before = context.State.CurrentMatch;

        var match = await LoadFromOverlayAsync(context, cancellationToken) ?? context.State.CurrentMatch;
        var swapped = match with { Player1 = match.Player2, Player2 = match.Player1 };
        context.State.SetCurrentMatch(swapped);

        var ok = await context.Overlay.ApplyPlayersAsync(swapped, cancellationToken);
        ok &= await context.Overlay.ApplyScoresAsync(swapped, cancellationToken);

        return ok
            ? CommandResult.Success("Players swapped.")
            : CommandResult.Fail("Players swapped locally but overlay update failed.");
    }

    public async Task<CommandResult> UndoAsync(CommandContext context, CancellationToken cancellationToken)
    {
        if (_before is null)
            return CommandResult.Fail("No previous match snapshot available.");

        context.State.SetCurrentMatch(_before);
        var ok = await context.Overlay.ApplyPlayersAsync(_before, cancellationToken);
        ok &= await context.Overlay.ApplyScoresAsync(_before, cancellationToken);

        return ok
            ? CommandResult.Success("Players restored.")
            : CommandResult.Fail("Players restored locally but overlay update failed.");
    }

    private static async Task<TournamentAutomation.Domain.MatchState?> LoadFromOverlayAsync(CommandContext context, CancellationToken cancellationToken)
    {
        var map = context.Config.Overlay;

        var p1Name = await context.Obs.GetTextAsync(map.P1Name, cancellationToken);
        var p1Team = await context.Obs.GetTextAsync(map.P1Team, cancellationToken);
        var p1Country = await context.Obs.GetTextAsync(map.P1Country, cancellationToken);
        var p1Flag = await context.Obs.GetImageFileAsync(map.P1Flag, cancellationToken);
        var p1ScoreText = await context.Obs.GetTextAsync(map.P1Score, cancellationToken);

        var p2Name = await context.Obs.GetTextAsync(map.P2Name, cancellationToken);
        var p2Team = await context.Obs.GetTextAsync(map.P2Team, cancellationToken);
        var p2Country = await context.Obs.GetTextAsync(map.P2Country, cancellationToken);
        var p2Flag = await context.Obs.GetImageFileAsync(map.P2Flag, cancellationToken);
        var p2ScoreText = await context.Obs.GetTextAsync(map.P2Score, cancellationToken);

        if (p1Name is null || p1Team is null || p1Country is null || p1ScoreText is null || p1Flag is null
            || p2Name is null || p2Team is null || p2Country is null || p2ScoreText is null || p2Flag is null)
        {
            return null;
        }

        _ = int.TryParse(p1ScoreText, out var p1Score);
        _ = int.TryParse(p2ScoreText, out var p2Score);

        var current = context.State.CurrentMatch;
        var meta = context.Config.Metadata;
        var p1CountryId = meta.ResolveCountry(p1Country, p1Flag);
        var p2CountryId = meta.ResolveCountry(p2Country, p2Flag);

        return current with
        {
            Player1 = current.Player1 with { Name = p1Name, Team = p1Team, Country = p1CountryId, Score = p1Score },
            Player2 = current.Player2 with { Name = p2Name, Team = p2Team, Country = p2CountryId, Score = p2Score }
        };
    }
}
