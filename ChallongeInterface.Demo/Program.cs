using ChallongeInterface;
using ChallongeInterface.Models;

var apiKey = "92a4874c22b7dbb76d7d1bff1dcd332e6bd4a15bfda3664b";
var tournament = "GagaTestingTournament";

if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(tournament))
{
    Console.WriteLine("Set CHALLONGE_API_KEY and CHALLONGE_TOURNAMENT before running the demo.");
    return;
}

var httpClient = new HttpClient();
var options = new ChallongeClientOptions
{
    ApiKey = apiKey
};

var client = new ChallongeClient(httpClient, options);

try
{
    IReadOnlyList<Participant> participants = await client.GetParticipantsAsync(tournament);
    IReadOnlyList<Match> matches = await client.GetMatchesAsync(tournament);
    var participantsById = participants.ToDictionary(p => p.Id);

    Console.WriteLine($"Tournament: {tournament}");
    Console.WriteLine($"Participants: {participants.Count}");
    Console.WriteLine($"Matches: {matches.Count}");

    var firstMatch = matches.FirstOrDefault();
    if (firstMatch is not null)
    {
        var player1Name = ResolveName(participantsById, firstMatch.Player1Id);
        var player2Name = ResolveName(participantsById, firstMatch.Player2Id);
        Console.WriteLine($"Sample match: {firstMatch.Identifier} ({player1Name} vs {player2Name})");
    }
}
catch (ChallongeApiException ex)
{
    Console.WriteLine($"API error: {ex.Message}");
    Console.WriteLine(ex.ResponseBody);
}

static string ResolveName(IReadOnlyDictionary<long, Participant> participants, long? playerId)
{
    if (playerId is null)
    {
        return "TBD";
    }

    return participants.TryGetValue(playerId.Value, out var participant)
        ? participant.DisplayName ?? participant.Name ?? participant.Username ?? participant.Id.ToString()
        : playerId.Value.ToString();
}
