using System.Net;
using System.Text;

namespace DungeonForge.Web.Tests;

internal sealed class FakeHandler(HttpStatusCode status, string body = "", Func<string>? bodyFactory = null) : HttpMessageHandler
{
    public HttpRequestMessage? LastRequest { get; private set; }
    public string? LastRequestBody { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastRequest = request;
        LastRequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        return new HttpResponseMessage(status)
        {
            Content = new StringContent(bodyFactory?.Invoke() ?? body, Encoding.UTF8, "application/json")
        };
    }

    public HttpClient CreateClient() => new(this) { BaseAddress = new Uri("http://localhost:5062") };
}
