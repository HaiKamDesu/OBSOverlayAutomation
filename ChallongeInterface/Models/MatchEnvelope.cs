using Newtonsoft.Json;

namespace ChallongeInterface.Models;

public sealed class MatchEnvelope
{
    [JsonProperty("match")]
    public Match? Match { get; init; }
}
