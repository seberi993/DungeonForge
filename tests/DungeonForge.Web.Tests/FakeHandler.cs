using System.Net;
using System.Text;

namespace DungeonForge.Web.Tests;

internal sealed class FakeHandler(HttpStatusCode status, string body = "", Func<string>? bodyFactory = null) : HttpMessageHandler
{
    public HttpRequestMessage? LastRequest { get; private set; }
    public string? LastRequestBody { get; private set; }
    // Spell lookups (the room preloads them for the mana estimate) are tracked apart so they never disturb turn-request assertions.
    public HttpRequestMessage? LastSpellsRequest { get; private set; }
    public bool SeparateSpells { get; set; }
    public string? SpellsBody { get; set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
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
