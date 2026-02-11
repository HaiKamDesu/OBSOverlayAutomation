using TournamentAutomation.Domain;

namespace TournamentAutomation.Configuration;

public sealed class OverlayMetadata
{
    public Dictionary<CountryId, CountryInfo> Countries { get; init; } = new();
    public Dictionary<FGCharacterId, FGCharacterInfo> Characters { get; init; } = new();

    public CountryInfo GetCountry(CountryId id)
        => Countries.TryGetValue(id, out var info) ? info : new CountryInfo { Id = CountryId.Unknown };

    public FGCharacterInfo GetCharacter(FGCharacterId id)
        => Characters.TryGetValue(id, out var info) ? info : new FGCharacterInfo { Id = FGCharacterId.Unknown };

    public CountryId ResolveCountry(string? acronym, string? flagPath)
    {
        var code = acronym?.Trim() ?? string.Empty;
        var flag = flagPath?.Trim() ?? string.Empty;

        foreach (var entry in Countries.Values)
        {
            if (string.Equals(entry.Acronym, code, StringComparison.OrdinalIgnoreCase))
                return entry.Id;
        }

        foreach (var entry in Countries.Values)
        {
            if (!string.IsNullOrWhiteSpace(flag) && string.Equals(entry.FlagPath, flag, StringComparison.OrdinalIgnoreCase))
                return entry.Id;
        }

        return CountryId.Unknown;
    }
}
