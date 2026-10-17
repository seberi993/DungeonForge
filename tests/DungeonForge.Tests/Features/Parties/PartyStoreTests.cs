using DungeonForge.Api.Domain;
using DungeonForge.Api.Features.Parties;
using DungeonForge.Tests.Features.Parties.Chaos;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;

namespace DungeonForge.Tests.Features.Parties;

public class PartyStoreTests
{
    // The API's cache has a size limit, so every entry must declare a size or Set throws.
    private static (PartyStore Store, MemoryCache Cache) Create(long sizeLimit = 1024)
    {
        var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = sizeLimit });
        return (new PartyStore(cache), cache);
    }

    private static Party NewParty(string inviteCode = "") =>
        Party.Create(Guid.NewGuid(), "Heroes", ChaosTestSupport.Roster(), DateTimeOffset.UtcNow, inviteCode);

    [Fact]
    public void Save_ThenTryGet_ReturnsTheSameInstance()
    {
        var (store, _) = Create();
        var party = NewParty();

        store.Save(party);

        store.TryGet(party.Id, out var found).Should().BeTrue();
        found.Should().BeSameAs(party);
    }

    [Fact]
    public void TryGet_UnknownId_IsFalse_WithNoParty()
    {
        var (store, _) = Create();

        store.TryGet(Guid.NewGuid(), out var found).Should().BeFalse();
        found.Should().BeNull();
    }

    [Fact]
    public void Save_UsesTheStablePartyKey_AndDeclaresASize()
    {
        var (store, cache) = Create(sizeLimit: 1);
        var party = NewParty();

        var save = () => store.Save(party);

        save.Should().NotThrow();
        cache.TryGetValue($"party:{party.Id}", out Party? cached).Should().BeTrue();
        cached.Should().BeSameAs(party);
    }

    [Fact]
    public void Register_StoresThePartyAndItsInviteCode()
    {
        var (store, cache) = Create();
        var party = NewParty("ABCD2345");

        store.Register(party, "ABCD2345");

        store.TryGetByInvite("ABCD2345", out var byCode).Should().BeTrue();
        byCode.Should().BeSameAs(party);
        store.InviteCodeTaken("ABCD2345").Should().BeTrue();
        store.InviteCodeTaken("ZZZZ2345").Should().BeFalse();
        cache.TryGetValue("invite:ABCD2345", out Guid id).Should().BeTrue();
        id.Should().Be(party.Id);
    }

    [Fact]
    public void TryGetByInvite_AnExpiredPartyBehindALiveCode_IsNotFound()
    {
        var (store, cache) = Create();
        var party = NewParty("ABCD2345");
        store.Register(party, "ABCD2345");
        cache.Remove($"party:{party.Id}");

        store.TryGetByInvite("ABCD2345", out var found).Should().BeFalse();
        found.Should().BeNull();
    }

    [Fact]
    public void TryGetByInvite_UnknownCode_IsFalse()
    {
        var (store, _) = Create();

        store.TryGetByInvite("ABCD2345", out _).Should().BeFalse();
    }

    [Fact]
    public void Touch_KeepsTheInviteAlive_AndIsHarmlessWithoutOne()
    {
        var (store, _) = Create();
        var withCode = NewParty("ABCD2345");
        store.Register(withCode, "ABCD2345");

        store.Touch(withCode);
        store.Touch(NewParty());

        store.InviteCodeTaken("ABCD2345").Should().BeTrue();
    }
}
