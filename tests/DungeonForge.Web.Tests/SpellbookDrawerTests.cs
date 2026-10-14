using System.Net;
using Bunit;
using DungeonForge.Web.Components;
using DungeonForge.Web.Models;
using DungeonForge.Web.Services;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace DungeonForge.Web.Tests;

public class SpellbookDrawerTests : TestContext
{
    private const string SpellsJson =
        """[{"name":"Fireball","classId":"mage","element":"fire","manaCost":8,"description":"Hurls flame."},{"name":"Frost Nova","classId":"mage","element":"ice","manaCost":6,"description":"Freezes foes."}]""";

    private FakeHandler Setup(HttpStatusCode status = HttpStatusCode.OK, string body = SpellsJson)
    {
        var handler = new FakeHandler(status, body);
        Services.AddSingleton(new DungeonApiClient(handler.CreateClient()));
        return handler;
    }

    [Fact]
    public void Closed_DoesNotCallApi_AndHasNoSpells()
    {
        var handler = Setup();
        var cut = RenderComponent<SpellbookDrawer>(p => p.Add(c => c.Class, CharacterClass.Mage).Add(c => c.Open, false));

        handler.LastRequest.Should().BeNull();
        cut.FindAll(".spell").Should().BeEmpty();
        cut.Find(".spellbook-drawer").ClassList.Should().NotContain("open");
    }

    [Fact]
    public void Open_LoadsSpellsForClass_AndRendersDetails()
    {
        var handler = Setup();
        var cut = RenderComponent<SpellbookDrawer>(p => p.Add(c => c.Class, CharacterClass.Mage).Add(c => c.Open, true));

        cut.WaitForAssertion(() => cut.FindAll(".spell").Should().HaveCount(2));
        handler.LastRequest!.RequestUri!.AbsolutePath.Should().Be("/api/classes/mage/spells");
        cut.Find(".spellbook-drawer").ClassList.Should().Contain("open");
        var first = cut.FindAll(".spell").ToList()[0];
        first.QuerySelector(".spell-name")!.TextContent.Should().Be("Fireball");
        first.QuerySelector(".spell-element")!.TextContent.Should().Be("Fire");
        first.QuerySelector(".spell-element")!.ClassList.Should().Contain("element-fire");
        first.QuerySelector(".spell-mana")!.TextContent.Should().Contain("8");
        first.QuerySelector(".spell-description")!.TextContent.Should().Be("Hurls flame.");
    }

    [Fact]
    public void Spells_AreReadOnly_NoButtonsAndHintToTypeThem()
    {
        Setup();
        var cut = RenderComponent<SpellbookDrawer>(p => p.Add(c => c.Class, CharacterClass.Mage).Add(c => c.Open, true));
        cut.WaitForAssertion(() => cut.FindAll(".spell").Should().HaveCount(2));

        cut.FindAll(".spell button").Should().BeEmpty();
        cut.Find(".spellbook-hint").TextContent.Should().Contain("I cast Fireball");
    }

    [Fact]
    public void ApiFailure_ShowsError()
    {
        Setup(HttpStatusCode.InternalServerError, "");
        var cut = RenderComponent<SpellbookDrawer>(p => p.Add(c => c.Class, CharacterClass.Mage).Add(c => c.Open, true));

        cut.WaitForAssertion(() => cut.Find(".spellbook-error").TextContent.Should().NotBeNullOrWhiteSpace());
    }

    [Fact]
    public void CloseButton_FiresOnClose()
    {
        Setup();
        var closed = false;
        var cut = RenderComponent<SpellbookDrawer>(p => p
            .Add(c => c.Class, CharacterClass.Mage).Add(c => c.Open, true)
            .Add(c => c.OnClose, () => closed = true));

        cut.Find(".spellbook-close").Click();

        closed.Should().BeTrue();
    }
}
