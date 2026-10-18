using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DungeonForge.Tests.Errors;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace DungeonForge.Tests.Features.Social;

public class PreferencesSliceTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private static Task<HttpResponseMessage> SetAsync(SocialHarness h, Guid id, object body) =>
        h.Client.PostAsJsonAsync($"/api/parties/{id}/social/preferences", body);

    [Fact]
    public async Task Preferences_AreSaved_ShownBackInTheOverview_AndPartiallyUpdatable()
    {
        using var h = SocialHarness.Create(factory);
        var id = await h.PartyAsync();

        var response = await SetAsync(h, id, new { playerName = "Conan", optOut = true });
        response.EnsureSuccessStatusCode();
        (await response.Content.ReadAsStringAsync()).Should().Be("{\"optOut\":true,\"debriefOptIn\":false}");
        (await SetAsync(h, id, new { playerName = "Conan", debriefOptIn = true })).EnsureSuccessStatusCode();

        using var me = await h.MeAsync(id, "Conan");
        me.RootElement.GetProperty("optedOut").GetBoolean().Should().BeTrue("a field left out is not changed");
        me.RootElement.GetProperty("debriefOptIn").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task AnOptedOutPlayer_IsNeverAskedAboutAndAsksAboutNobody()
    {
        using var h = SocialHarness.Create(factory);
        var id = await h.PartyAsync();
        (await SetAsync(h, id, new { playerName = "Dru", optOut = true })).EnsureSuccessStatusCode();

        foreach (var rater in new[] { "Conan", "Merlin", "Lyra" })
        {
            for (var i = 0; i < 3; i++)
            {
                var response = await h.Client.GetAsync($"/api/parties/{id}/social/peer-prompt?playerName={rater}");
                response.EnsureSuccessStatusCode();
                using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                doc.RootElement.GetProperty("about").GetString().Should().NotBe("Dru");
                (await h.Client.PostAsJsonAsync($"/api/parties/{id}/social/peer-answer",
                    new { playerName = rater, promptId = doc.RootElement.GetProperty("promptId").GetString(), choice = "yes" })).EnsureSuccessStatusCode();
            }
        }

        using var problem = await ProblemBody.ReadAsync(
            await h.Client.GetAsync($"/api/parties/{id}/social/peer-prompt?playerName=Dru"), HttpStatusCode.Conflict);
        problem.Code.Should().Be("social.no_peer_prompt");
    }

    [Fact]
    public async Task AnOptedOutPlayer_CannotBeWhisperedTo_AndCannotWhisper()
    {
        using var h = SocialHarness.Create(factory);
        var id = await h.PartyAsync();
        (await SetAsync(h, id, new { playerName = "Dru", optOut = true })).EnsureSuccessStatusCode();

        using var toThem = await ProblemBody.ReadAsync(await h.AskAsync(id, "Conan", ["Dru"], "Psst"), HttpStatusCode.BadRequest);
        using var fromThem = await ProblemBody.ReadAsync(await h.AskAsync(id, "Dru", ["Conan"], "Psst"), HttpStatusCode.NotFound);

        toThem.Code.Should().Be("social.invalid_recipients");
        fromThem.Code.Should().Be("social.module_disabled");
    }

    [Fact]
    public async Task OptingBackIn_RestoresEverything()
    {
        using var h = SocialHarness.Create(factory);
        var id = await h.PartyAsync();
        (await SetAsync(h, id, new { playerName = "Dru", optOut = true })).EnsureSuccessStatusCode();
        (await SetAsync(h, id, new { playerName = "Dru", optOut = false })).EnsureSuccessStatusCode();

        (await h.AskAsync(id, "Conan", ["Dru"], "Psst")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Preferences_ValidateTheBody_AndUnknownPlayersAreRejected()
    {
        using var h = SocialHarness.Create(factory);
        var id = await h.PartyAsync();

        using var empty = await ProblemBody.ReadAsync(await SetAsync(h, id, new { playerName = "" }), HttpStatusCode.BadRequest);
        using var stranger = await ProblemBody.ReadAsync(await SetAsync(h, id, new { playerName = "Nobody", optOut = true }), HttpStatusCode.BadRequest);

        empty.Code.Should().Be("validation.invalid_request");
        stranger.Code.Should().Be("party.player_unknown");
    }
}
