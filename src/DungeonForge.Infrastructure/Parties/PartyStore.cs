using DungeonForge.Application.Features.Parties;
using System.Diagnostics.CodeAnalysis;
using DungeonForge.Domain;
using Microsoft.Extensions.Caching.Memory;

namespace DungeonForge.Infrastructure.Parties;

public sealed class PartyStore(IMemoryCache cache) : IPartyStore
{
    // Size is mandatory (cache SizeLimit); at the limit a Set may be dropped silently.
    private static MemoryCacheEntryOptions EntryOptions() => new()
    {
        Size = 1,
        SlidingExpiration = TimeSpan.FromHours(2),
    };

    private static string PartyKey(Guid id) => $"party:{id}";

    private static string InviteKey(string normalizedCode) => $"invite:{normalizedCode}";

    public bool TryGet(Guid id, [NotNullWhen(true)] out Party? party) =>
        cache.TryGetValue(PartyKey(id), out party) && party is not null;

    public bool TryGetByInvite(string normalizedCode, [NotNullWhen(true)] out Party? party)
    {
        party = null;
        return cache.TryGetValue(InviteKey(normalizedCode), out Guid id) && TryGet(id, out party);
    }

    public bool InviteCodeTaken(string normalizedCode) => cache.TryGetValue(InviteKey(normalizedCode), out _);

    public void Save(Party party) => cache.Set(PartyKey(party.Id), party, EntryOptions());

    public void Register(Party party, string normalizedInviteCode)
    {
        Save(party);
        cache.Set(InviteKey(normalizedInviteCode), party.Id, EntryOptions());
    }

    public void Touch(Party party)
    {
        if (party.InviteCode.Length > 0)
        {
            cache.TryGetValue(InviteKey(party.InviteCode), out _);
        }
    }
}
