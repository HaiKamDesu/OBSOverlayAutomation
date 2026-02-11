namespace TournamentAutomation.Domain;

public enum FGCharacterId
{
    Unknown = 0,
    Ragna,
    Jin,
    Noel,
    Rachel
}

public sealed record FGCharacterInfo
{
    public FGCharacterId Id { get; init; } = FGCharacterId.Unknown;
    public string ImagePath { get; init; } = "";
}
