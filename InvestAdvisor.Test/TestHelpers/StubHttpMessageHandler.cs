using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace InvestAdvisor.Test.TestHelpers;

/// <summary>
/// Records outbound requests and returns a canned response. Used for typed-HttpClient tests
/// that need to assert request shape without hitting the network.
/// </summary>
public sealed class StubHttpMessageHandler : HttpMessageHandler
{
    public string ResponseBody { get; set; } = "{}";
    public HttpStatusCode StatusCode { get; set; } = HttpStatusCode.OK;
    public string MediaType { get; set; } = "application/json";

    public HttpRequestMessage? LastRequest { get; private set; }
    public string? LastRequestBody { get; private set; }
    public int CallCount { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        CallCount++;
        LastRequest = request;
        LastRequestBody = request.Content is null
            ? null
            : await request.Content.ReadAsStringAsync(cancellationToken);

        var response = new HttpResponseMessage(StatusCode)
        {
            Content = new StringContent(ResponseBody, Encoding.UTF8),
        };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue(MediaType);
        return response;
    }
}
