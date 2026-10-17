using System.Net;
using System.Text;

namespace DungeonForge.Web.Tests;

public sealed class FakeHandler(HttpStatusCode status, string body = "", Func<string>? bodyFactory = null) : HttpMessageHandler
{
    public HttpRequestMessage? LastRequest { get; private set; }
    public string? LastRequestBody { get; private set; }
    // Spell lookups (the room preloads them for the mana estimate) are tracked apart so they never disturb turn-request assertions.
    public HttpRequestMessage? LastSpellsRequest { get; private set; }
    public bool SeparateSpells { get; set; }
    public string? SpellsBody { get; set; }

    // Social calls (the room's panel loads them on its own) are tracked apart too. With no scripted route they answer 404,
    // exactly like a server with the social game off, so the panel stays hidden and turn assertions are never disturbed.
    public List<(HttpMethod Method, string PathAndQuery, string? Body)> SocialRequests { get; } = [];
    public Dictionary<string, (HttpStatusCode Status, string Body)> SocialRoutes { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.RequestUri!.AbsolutePath.Contains("/social/", StringComparison.Ordinal))
        {
            var requestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            lock (SocialRequests) { SocialRequests.Add((request.Method, request.RequestUri.PathAndQuery, requestBody)); }
            var key = $"{request.Method} {request.RequestUri.AbsolutePath}";
            var (status, json) = SocialRoutes.GetValueOrDefault(key, (HttpStatusCode.NotFound, "{}"));
            return new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }

        if (SeparateSpells && request.Method == HttpMethod.Get && request.RequestUri!.AbsolutePath.EndsWith("/spells", StringComparison.Ordinal))
        {
            LastSpellsRequest = request;
            var spells = SpellsBody ?? (body.TrimStart().StartsWith('[') ? body : "[]");
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(spells, Encoding.UTF8, "application/json") };
        }

        LastRequest = request;
        LastRequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        return new HttpResponseMessage(status)
        {
            Content = new StringContent(bodyFactory?.Invoke() ?? body, Encoding.UTF8, "application/json")
        };
    }

    public HttpClient CreateClient() => new(this) { BaseAddress = new Uri("http://localhost:5062") };
}
