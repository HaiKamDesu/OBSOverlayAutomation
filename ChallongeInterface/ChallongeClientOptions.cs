namespace ChallongeInterface;

public sealed class ChallongeClientOptions
{
    public string ApiKey { get; init; } = string.Empty;
    public Uri BaseUri { get; init; } = new("https://api.challonge.com/v1/");
}
