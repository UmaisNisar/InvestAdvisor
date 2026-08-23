using System.Net;

namespace InvestAdvisor.Test.TestHelpers;

/// <summary>One-liner for the typed-HttpClient tests: a stub handler with a canned body plus the client over it.</summary>
internal static class HttpSut
{
    public static (HttpClient Http, StubHttpMessageHandler Handler) Json(
        string body = "{}", HttpStatusCode status = HttpStatusCode.OK, string? baseAddress = null, string mediaType = "application/json")
    {
        var handler = new StubHttpMessageHandler { ResponseBody = body, StatusCode = status, MediaType = mediaType };
        var http = new HttpClient(handler);
        if (baseAddress is not null) http.BaseAddress = new Uri(baseAddress);
        return (http, handler);
    }
}
