using System.Net;
using System.Text;
using ChallongeInterface;
using ChallongeInterface.Models;
using Xunit;

namespace ChallongeInterface.Tests;

public sealed class ChallongeClientTests
{
    [Fact]
    public async Task GetParticipantsAsync_ParsesParticipants()
    {
        var handler = new TestHttpMessageHandler(request =>
        {
            Assert.Equal("https", request.RequestUri?.Scheme);
            Assert.Contains("/v1/tournaments/my-tourney/participants.json", request.RequestUri?.AbsolutePath);
            Assert.Contains("api_key=test-key", request.RequestUri?.Query);

            var json = "[{'participant':{'id':1,'name':'Alice'}},{'participant':{'id':2,'name':'Bob'}}]";
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json.Replace('\'', '"'), Encoding.UTF8, "application/json")
            };
        });

        var client = CreateClient(handler);

        IReadOnlyList<Participant> participants = await client.GetParticipantsAsync("my-tourney");

        Assert.Equal(2, participants.Count);
        Assert.Equal("Alice", participants[0].Name);
        Assert.Equal(2, participants[1].Id);
    }

    [Fact]
    public async Task GetParticipantAsync_RequestsIncludeMatchesWhenEnabled()
    {
        var handler = new TestHttpMessageHandler(request =>
        {
            Assert.Contains("/v1/tournaments/my-tourney/participants/99.json", request.RequestUri?.AbsolutePath);
            Assert.Contains("include_matches=1", request.RequestUri?.Query);

            var json = "{'participant':{'id':99,'name':'Zed','display_name_with_invitation_email_address':'Zed (invited)'}}";
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json.Replace('\'', '\"'), Encoding.UTF8, "application/json")
            };
        });

        var client = CreateClient(handler);

        Participant participant = await client.GetParticipantAsync("my-tourney", 99, includeMatches: true);

        Assert.Equal(99, participant.Id);
        Assert.Equal("Zed", participant.Name);
        Assert.Equal("Zed (invited)", participant.DisplayNameWithInvitationEmailAddress);
        Assert.Equal("Zed", participant.DisplayName);
    }

    [Fact]
    public async Task GetMatchesAsync_AddsOptionalFilters()
    {
        var handler = new TestHttpMessageHandler(request =>
        {
            Assert.Contains("state=underway", request.RequestUri?.Query);
            Assert.Contains("participant_id=42", request.RequestUri?.Query);

            var json = "[{'match':{'id':10,'identifier':'A1','player1_id':1,'player2_id':2}}]";
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json.Replace('\'', '"'), Encoding.UTF8, "application/json")
            };
        });

        var client = CreateClient(handler);

        IReadOnlyList<Match> matches = await client.GetMatchesAsync("my-tourney", "underway", 42);

        Assert.Single(matches);
        Assert.Equal("A1", matches[0].Identifier);
    }

    [Fact]
    public async Task GetParticipantsAsync_ThrowsOnError()
    {
        var handler = new TestHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = new StringContent("{\"error\":\"bad api key\"}")
        });

        var client = CreateClient(handler);

        var ex = await Assert.ThrowsAsync<ChallongeApiException>(() => client.GetParticipantsAsync("my-tourney"));

        Assert.Equal(HttpStatusCode.Forbidden, ex.StatusCode);
        Assert.Contains("bad api key", ex.ResponseBody);
    }

    private static ChallongeClient CreateClient(TestHttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.challonge.com/")
        };

        var options = new ChallongeClientOptions
        {
            ApiKey = "test-key"
        };

        return new ChallongeClient(httpClient, options);
    }

    private sealed class TestHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public TestHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_handler(request));
        }
    }
}
