namespace TournamentAutomation.Domain;

public enum CountryId
{
    Unknown = 0,
    ARG,
    USA,
    JPN,
    MEX,
    CHL
}

public sealed record CountryInfo
{
    public CountryId Id { get; init; } = CountryId.Unknown;
    public string Acronym { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public string FlagPath { get; init; } = "";
}
