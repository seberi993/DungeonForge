using System.Net;
using System.Text.Json;
using FluentAssertions;

namespace DungeonForge.Tests.Errors;

// Reads an error response and checks the fields every ProblemDetails from this API carries.
internal sealed class ProblemBody(JsonDocument doc) : IDisposable
{
    public JsonElement Root => doc.RootElement;

    public string Code => Root.GetProperty("code").GetString()!;

    public string Operation => Root.GetProperty("operation").GetString()!;

    public string Parameter(string key) => Root.GetProperty("parameters").GetProperty(key).GetString()!;

    public static async Task<ProblemBody> ReadAsync(HttpResponseMessage response, HttpStatusCode status)
    {
        response.StatusCode.Should().Be(status);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        var body = new ProblemBody(JsonDocument.Parse(await response.Content.ReadAsStringAsync()));
        body.Root.GetProperty("status").GetInt32().Should().Be((int)status);
        body.Root.GetProperty("traceId").GetString().Should().NotBeNullOrEmpty();
        return body;
    }

    public void Dispose() => doc.Dispose();
}
