using System.Net;
using System.Text;
using ChallongeInterface.Models;
using Newtonsoft.Json;
using System.Net.Http.Headers;

namespace ChallongeInterface;

public sealed class ChallongeClient
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly Uri _baseUri;

    public ChallongeClient(HttpClient httpClient, ChallongeClientOptions options)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            throw new ArgumentException("ApiKey is required.", nameof(options));
        }

        _apiKey = options.ApiKey;
        _baseUri = options.BaseUri;
    }

    public async Task<IReadOnlyList<Participant>> GetParticipantsAsync(
        string tournament,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(tournament))
        {
            throw new ArgumentException("Tournament is required.", nameof(tournament));
        }

        var path = $"tournaments/{Uri.EscapeDataString(tournament)}/participants.json";
        var requestUri = BuildUri(path, new Dictionary<string, string?>());

        var response = await _httpClient.GetAsync(requestUri, cancellationToken).ConfigureAwait(false);
        var payload = await ReadOrThrowAsync(response, cancellationToken).ConfigureAwait(false);

        var envelopes = JsonConvert.DeserializeObject<List<ParticipantEnvelope>>(payload)
            ?? new List<ParticipantEnvelope>();

        return envelopes
            .Select(e => e.Participant)
            .Where(p => p is not null)
            .Select(p => p!)
            .ToList();
    }

    public async Task<IReadOnlyList<Match>> GetMatchesAsync(
        string tournament,
        string? state = null,
        long? participantId = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(tournament))
        {
            throw new ArgumentException("Tournament is required.", nameof(tournament));
        }

        var path = $"tournaments/{Uri.EscapeDataString(tournament)}/matches.json";
        var query = new Dictionary<string, string?>
        {
            ["state"] = string.IsNullOrWhiteSpace(state) ? null : state,
            ["participant_id"] = participantId?.ToString()
        };

        var requestUri = BuildUri(path, query);

        var response = await _httpClient.GetAsync(requestUri, cancellationToken).ConfigureAwait(false);
        var payload = await ReadOrThrowAsync(response, cancellationToken).ConfigureAwait(false);

        var envelopes = JsonConvert.DeserializeObject<List<MatchEnvelope>>(payload)
            ?? new List<MatchEnvelope>();

        return envelopes
            .Select(e => e.Match)
            .Where(m => m is not null)
            .Select(m => m!)
            .ToList();
    }

    public async Task<Participant> GetParticipantAsync(
        string tournament,
        long participantId,
        bool includeMatches = false,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(tournament))
        {
            throw new ArgumentException("Tournament is required.", nameof(tournament));
        }

        var path = $"tournaments/{Uri.EscapeDataString(tournament)}/participants/{participantId}.json";
        var query = new Dictionary<string, string?>
        {
            ["include_matches"] = includeMatches ? "1" : null
        };

        var requestUri = BuildUri(path, query);

        var response = await _httpClient.GetAsync(requestUri, cancellationToken).ConfigureAwait(false);
        var payload = await ReadOrThrowAsync(response, cancellationToken).ConfigureAwait(false);

        var envelope = JsonConvert.DeserializeObject<ParticipantEnvelope>(payload);
        if (envelope?.Participant is null)
        {
            throw new InvalidOperationException("Challonge API returned an empty participant payload.");
        }

        return envelope.Participant;
    }

    public async Task<Match> UpdateMatchAsync(
        string tournament,
        long matchId,
        string scoresCsv,
        long winnerId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(tournament))
            throw new ArgumentException("Tournament is required.", nameof(tournament));

        if (string.IsNullOrWhiteSpace(scoresCsv))
            throw new ArgumentException("scoresCsv is required.", nameof(scoresCsv));

        var path = $"tournaments/{Uri.EscapeDataString(tournament)}/matches/{matchId}.json";
        var requestUri = BuildUri(path, new Dictionary<string, string?>());
        var body = new Dictionary<string, string>
        {
            ["match[scores_csv]"] = scoresCsv,
            ["match[winner_id]"] = winnerId.ToString()
        };

        using var request = new HttpRequestMessage(HttpMethod.Put, requestUri)
        {
            Content = new FormUrlEncodedContent(body)
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var payload = await ReadOrThrowAsync(response, cancellationToken).ConfigureAwait(false);

        var envelope = JsonConvert.DeserializeObject<MatchEnvelope>(payload);
        if (envelope?.Match is null)
            throw new InvalidOperationException("Challonge API returned an empty match payload.");

        return envelope.Match;
    }

    private Uri BuildUri(string relativePath, Dictionary<string, string?> query)
    {
        query["api_key"] = _apiKey;

        var builder = new UriBuilder(new Uri(_baseUri, relativePath));
        var queryString = BuildQueryString(query);
        builder.Query = queryString;
        return builder.Uri;
    }

    private static string BuildQueryString(Dictionary<string, string?> query)
    {
        var sb = new StringBuilder();
        foreach (var kvp in query)
        {
            if (string.IsNullOrWhiteSpace(kvp.Value))
            {
                continue;
            }

            if (sb.Length > 0)
            {
                sb.Append('&');
            }

            sb.Append(Uri.EscapeDataString(kvp.Key));
            sb.Append('=');
            sb.Append(Uri.EscapeDataString(kvp.Value));
        }

        return sb.ToString();
    }

    private static async Task<string> ReadOrThrowAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new ChallongeApiException(response.StatusCode, payload);
        }

        return payload;
    }
}
