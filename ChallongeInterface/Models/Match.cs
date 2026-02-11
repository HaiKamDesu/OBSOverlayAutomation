using Newtonsoft.Json;

namespace ChallongeInterface.Models;

public sealed class Match
{
    [JsonProperty("id")]
    public long Id { get; init; }

    [JsonProperty("state")]
    public string? State { get; init; }

    [JsonProperty("player1_id")]
    public long? Player1Id { get; init; }

    [JsonProperty("player2_id")]
    public long? Player2Id { get; init; }

    [JsonProperty("winner_id")]
    public long? WinnerId { get; init; }

    [JsonProperty("loser_id")]
    public long? LoserId { get; init; }

    [JsonProperty("round")]
    public int? Round { get; init; }

    [JsonProperty("identifier")]
    public string? Identifier { get; init; }

    [JsonProperty("suggested_play_order")]
    public int? SuggestedPlayOrder { get; init; }

    [JsonProperty("scores_csv")]
    public string? ScoresCsv { get; init; }
}
