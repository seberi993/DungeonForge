using System.Net;
using System.Text.Json;
using DungeonForge.Domain;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace DungeonForge.Tests.Features.Classes;

public sealed class GetClassSpellsTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    public static TheoryData<CharacterClass> AllClasses() => new(Enum.GetValues<CharacterClass>());

    [Theory]
    [MemberData(nameof(AllClasses))]
    public async Task EveryClass_ReturnsAtLeastOneSpell_WithMatchingClassId(CharacterClass cls)
    {
        var response = await factory.CreateClient().GetAsync($"/api/classes/{cls}/spells");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var spells = doc.RootElement.EnumerateArray().ToList();
        spells.Should().NotBeEmpty();
        spells.Should().OnlyContain(s => s.GetProperty("classId").GetString() == JsonNamingPolicy.CamelCase.ConvertName(cls.ToString()));
    }

    [Theory]
    [InlineData("MAGE")]
    [InlineData("mage")]
    [InlineData("1")]
    public async Task Id_AcceptsEnumNameCaseInsensitiveOrInteger(string id)
    {
        var response = await factory.CreateClient().GetAsync($"/api/classes/{id}/spells");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement[0].GetProperty("classId").GetString().Should().Be("mage");
    }

    [Theory]
    [InlineData("wizard")]
    [InlineData("7")]
    [InlineData("-1")]
    public async Task UnknownId_Returns404(string id)
    {
        var response = await factory.CreateClient().GetAsync($"/api/classes/{id}/spells");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Json_IsCamelCase_WithStringEnums()
    {
        var response = await factory.CreateClient().GetAsync("/api/classes/priest/spells");

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var spell = doc.RootElement[0];
        spell.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo("name", "classId", "element", "manaCost", "description");
        spell.GetProperty("element").ValueKind.Should().Be(JsonValueKind.String);
        spell.GetProperty("element").GetString().Should().MatchRegex("^[a-z]");
        spell.GetProperty("manaCost").GetInt32().Should().BeGreaterThan(0);
    }

    [Fact]
    public void Catalog_Invariants()
    {
        var all = Enum.GetValues<CharacterClass>().SelectMany(SpellCatalog.For).ToList();

        all.Should().OnlyContain(s => s.ManaCost > 0 && s.Name.Length > 0 && s.Description.Length > 0);
        all.Select(s => s.Name).Should().OnlyHaveUniqueItems();
        foreach (var cls in Enum.GetValues<CharacterClass>())
        {
            SpellCatalog.For(cls).Should().HaveCountGreaterThanOrEqualTo(3).And.OnlyContain(s => s.ClassId == cls);
        }
    }
}
