using TournamentAutomation.Domain;

namespace TournamentAutomation.Application.Commands;

public sealed class EditPlayerCommand : ICommand
{
    private readonly bool _isPlayerOne;
    private MatchState? _before;

    public EditPlayerCommand(bool isPlayerOne)
    {
        _isPlayerOne = isPlayerOne;
    }

    public bool RecordInHistory => true;
    public string Description => $"Edit {(_isPlayerOne ? "P1" : "P2")} info";

    public async Task<CommandResult> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        _before = context.State.CurrentMatch;
        var match = context.State.CurrentMatch;
        var current = _isPlayerOne ? match.Player1 : match.Player2;

        context.Logger.Info($"EDIT: Enter new values for {(_isPlayerOne ? "P1" : "P2")} (leave blank to keep current)." );

        var name = Prompt("Name", current.Name);
        var team = Prompt("Team", current.Team);
        var country = Prompt("Country (acronym)", context.Config.Metadata.GetCountry(current.Country).Acronym);
        var chars = Prompt("Characters (comma-separated IDs)", string.Join(",", current.Characters));

        var countryId = ResolveCountry(context, country, current.Country);
        var characterIds = ResolveCharacters(context, chars, current.Characters);

        var updated = current with
        {
            Name = name,
            Team = team,
            Country = countryId,
            Characters = characterIds
        };

        var updatedMatch = _isPlayerOne
            ? match with { Player1 = updated }
            : match with { Player2 = updated };

        context.State.SetCurrentMatch(updatedMatch);

        var ok = await context.Overlay.ApplyPlayersAsync(updatedMatch, cancellationToken);
        return ok
            ? CommandResult.Success("Player updated.")
            : CommandResult.Fail("Player updated locally but overlay update failed.");
    }

    public async Task<CommandResult> UndoAsync(CommandContext context, CancellationToken cancellationToken)
    {
        if (_before is null)
            return CommandResult.Fail("No previous player snapshot available.");

        context.State.SetCurrentMatch(_before);
        var ok = await context.Overlay.ApplyPlayersAsync(_before, cancellationToken);
        return ok
            ? CommandResult.Success("Player restored.")
            : CommandResult.Fail("Player restored locally but overlay update failed.");
    }

    private static string Prompt(string label, string current)
    {
        Console.Write($"{label} [{current}]: ");
        var input = Console.ReadLine();
        return string.IsNullOrWhiteSpace(input) ? current : input.Trim();
    }

    private static CountryId ResolveCountry(CommandContext context, string acronym, CountryId fallback)
    {
        var resolved = context.Config.Metadata.ResolveCountry(acronym, null);
        return resolved == CountryId.Unknown ? fallback : resolved;
    }

    private static IReadOnlyList<FGCharacterId> ResolveCharacters(CommandContext context, string input, IReadOnlyList<FGCharacterId> fallback)
    {
        if (string.IsNullOrWhiteSpace(input))
            return fallback;

        var tokens = input.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var list = new List<FGCharacterId>();

        foreach (var token in tokens)
        {
            if (Enum.TryParse<FGCharacterId>(token, true, out var id))
            {
                list.Add(id);
                continue;
            }

            var match = context.Config.Metadata.Characters
                .FirstOrDefault(x => string.Equals(x.Value.Id.ToString(), token, StringComparison.OrdinalIgnoreCase));
            if (!match.Equals(default(KeyValuePair<FGCharacterId, FGCharacterInfo>)))
                list.Add(match.Key);
        }

        return list.Count == 0 ? fallback : list;
    }
}
