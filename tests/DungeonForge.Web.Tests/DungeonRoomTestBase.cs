using System.Net;
using Bunit;
using Bunit.TestDoubles;
using DungeonForge.Web.Components;
using DungeonForge.Web.Models;
using DungeonForge.Web.Pages;
using DungeonForge.Web.Services;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace DungeonForge.Web.Tests;

/// <summary>Shared rendering, JSON builders and hub fake for the DungeonRoom behaviour tests.</summary>
public abstract class DungeonRoomTestBase : TestContext
{
    protected readonly GameSession _session = new();
    protected FakeNavigationManager Nav => Services.GetRequiredService<FakeNavigationManager>();
    protected static readonly Guid Id = Guid.Parse("22222222-2222-2222-2222-222222222222");

    protected DungeonRoomTestBase()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    protected FakeDungeonHubClient _hub = new();

    protected IRenderedComponent<DungeonRoom> Render(FakeHandler handler, bool withParty = true)
    {
        handler.SeparateSpells = true;
        Services.AddSingleton<IDungeonHubClient>(_hub);
        if (withParty) _session.Start(new PartyResponse(Id, "Heroes",
            [new("Conan", CharacterClass.Warrior, 30, 30, false), new("Merlin", CharacterClass.Mage, 16, 16, false)],
            DateTimeOffset.UtcNow, "K7F2-9QXM"));
        Services.AddSingleton(_session);
        Services.AddSingleton(new DungeonApiClient(handler.CreateClient()));
        return RenderComponent<DungeonRoom>();
    }

    protected static string ExploreJson(int turn, string room, string? target, int damage, int gold, string flavor, int totalGold, bool defeated = false) =>
        $$"""{"partyId":"{{Id}}","turn":{{turn}},"roomType":"{{room}}","targetName":{{(target is null ? "null" : $"\"{target}\"")}},"damage":{{damage}},"goldFound":{{gold}},"flavorEvent":"{{flavor}}","gold":{{totalGold}},"isPartyDefeated":{{(defeated ? "true" : "false")}}}""";

    protected static ExploreResponse Turn(int turn, RoomType room, string? target, int damage, int gold, string flavor, int totalGold, bool defeated = false) =>
        new(Id, turn, room, target, damage, gold, flavor, totalGold, defeated);

    protected static string PromptJson(string discussion = "[]") =>
        $$"""{"partyId":"{{Id}}","turn":0,"roomType":"combat","damage":0,"goldFound":0,"flavorEvent":"A troll blocks the bridge.\nIt growls.","gold":0,"isPartyDefeated":false,"scenario":"A troll blocks the bridge.\nIt growls.","scenarioPending":false,"discussion":{{discussion}}}""";

    protected static string PendingResolveJson() =>
        $$"""{"partyId":"{{Id}}","turn":1,"roomType":"puzzle","damage":0,"goldFound":20,"flavorEvent":"Solved","gold":20,"isPartyDefeated":false,"narrationPending":true}""";

    protected static string ResolveWithOutcomeJson() =>
        $$"""{"partyId":"{{Id}}","turn":1,"roomType":"combat","targetName":"Conan","damage":3,"goldFound":0,"flavorEvent":"Resolved","gold":0,"isPartyDefeated":false,"lastOutcome":"You bribed the troll and slipped past."}""";

    protected static readonly string Scenario0 = "A troll blocks the bridge.\nIt growls.";

    protected IRenderedComponent<DungeonRoom> RenderWithScenario(FakeHandler handler, TimeSpan? narrationTimeout = null)
    {
        handler.SeparateSpells = true;
        _session.Start(new PartyResponse(Id, "Heroes",
            [new("Conan", CharacterClass.Warrior, 30, 30, false), new("Merlin", CharacterClass.Mage, 16, 16, false)],
            DateTimeOffset.UtcNow, "K7F2-9QXM"));
        _session.ApplyPrompt(new ExploreResponse(Id, 0, RoomType.Combat, null, 0, 0, Scenario0, 0, false, Scenario: Scenario0));
        Services.AddSingleton<IDungeonHubClient>(_hub);
        Services.AddSingleton(_session);
        Services.AddSingleton(new DungeonApiClient(handler.CreateClient()));
        return RenderComponent<DungeonRoom>(p =>
        {
            if (narrationTimeout is { } t) p.Add(c => c.NarrationTimeout, t);
        });
    }

    protected static void Resolve(IRenderedComponent<DungeonRoom> cut, string text = "Attack the troll")
    {
        cut.Find("#action-text").Input(text);
        cut.Find("button#lock-in").Click();
    }
}
