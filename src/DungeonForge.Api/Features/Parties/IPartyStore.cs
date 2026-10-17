using System.Diagnostics.CodeAnalysis;
using DungeonForge.Api.Domain;

namespace DungeonForge.Api.Features.Parties;

// The one door to live parties. A party is a mutable object shared by reference: handlers change it under its own lock and call
// Save afterwards, which only refreshes the sliding expiry. Parties live in memory only, so a missing party also means an
// expired one.
public interface IPartyStore
{
    bool TryGet(Guid id, [NotNullWhen(true)] out Party? party);

    // Looks the party up through its normalized invite code (see InviteCodes.Normalize).
    bool TryGetByInvite(string normalizedCode, [NotNullWhen(true)] out Party? party);

    bool InviteCodeTaken(string normalizedCode);

    // Stores or re-stores the party; re-storing refreshes its sliding expiry.
    void Save(Party party);

    // A new party and its invite code. The party goes in first, so a resolvable code never points at a missing party.
    void Register(Party party, string normalizedInviteCode);

    // Keeps the invite code's expiry in step with an active party; does nothing for a party without one.
    void Touch(Party party);
}
