using Newtonsoft.Json;

namespace ChallongeInterface.Models;

public sealed class ParticipantEnvelope
{
    [JsonProperty("participant")]
    public Participant? Participant { get; init; }
}
