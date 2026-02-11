using System.Net;

namespace ChallongeInterface;

public sealed class ChallongeApiException : Exception
{
    public ChallongeApiException(HttpStatusCode statusCode, string responseBody)
        : base($"Challonge API returned {(int)statusCode} ({statusCode}).")
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }

    public HttpStatusCode StatusCode { get; }
    public string ResponseBody { get; }
}
