using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace InvestAdvisor.Test.TestHelpers;

/// <summary>
/// Returns a canned response chosen by matching the request URI against registered routes (first
/// substring match wins). Used for multi-call flows like Reddit's token-then-search sequence.
/// Records every outbound request for assertions.
/// </summary>
public sealed class RoutingHttpMessageHandler : HttpMessageHandler
{
    private readonly List<(string UrlContains, HttpStatusCode Status, string Body)> _routes = new();

    public List<HttpRequestMessage> Requests { get; } = new();

    public RoutingHttpMessageHandler When(string urlContains, string body, HttpStatusCode status = HttpStatusCode.OK)
    {
        _routes.Add((urlContains, status, body));
        return this;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        var uri = request.RequestUri?.ToString() ?? string.Empty;
        var route = _routes.FirstOrDefault(r => uri.Contains(r.UrlContains, StringComparison.OrdinalIgnoreCase));

        if (route.Body is null)
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("{}", Encoding.UTF8),
            });

        var resp = new HttpResponseMessage(route.Status)
        {
            Content = new StringContent(route.Body, Encoding.UTF8),
        };
        resp.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        return Task.FromResult(resp);
    }
}
