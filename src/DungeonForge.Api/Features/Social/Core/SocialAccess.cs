using DungeonForge.Api.Domain;
using DungeonForge.Api.Errors;
using DungeonForge.Api.Features.Parties;
using DungeonForge.Api.Features.Parties.Chaos;
using DungeonForge.Api.Features.Parties.Realtime;
using DungeonForge.Api.Features.Social.Abilities;
using Microsoft.Extensions.Caching.Memory;

namespace DungeonForge.Api.Features.Social.Core;

public sealed record SocialPlayer(Party Party, PartyMember Member);

// What every social slice does first: find the party, check the module is on, resolve the named player.
public sealed class SocialAccess(IMemoryCache cache, SocialModules modules)
{
    // A null module means "any part of the social game", used by the read-only overview.
    public Result<SocialPlayer> Resolve(Guid partyId, string? playerName, SocialModule? module, string operation)
    {
        if (!cache.TryGetValue(PartyCache.Key(partyId), out Party? party) || party is null)
        {
            return PartyErrors.NotFound(operation, partyId);
        }

        if (module is { } required ? !modules.IsEnabled(required) : !modules.Enabled)
        {
            return SocialErrors.ModuleDisabled(operation, partyId);
        }

        var name = playerName?.Trim() ?? string.Empty;
        var member = party.Members.FirstOrDefault(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase));
        return member is null ? PartyErrors.PlayerUnknown(operation, partyId, name) : new SocialPlayer(party, member);
    }

    // Refreshes the sliding expiry after a mutation, like every other party slice.
    public void Touch(Party party) => cache.Set(PartyCache.Key(party.Id), party, PartyCache.EntryOptions());
}

// Pushes what the lock-protected code queued: the narrator's party-wide lines and private words. Failures are logged by
// ChaosDispatcher (BestEffort) and never fail the request.
public sealed class SocialDelivery(ChaosDispatcher chaos)
{
    public async Task DeliverAsync(Party party)
    {
        IReadOnlyList<SocialNotice> notices;
        lock (party.SyncRoot)
        {
            notices = party.Social().DrainOutbox();
        }

        foreach (var notice in notices)
        {
            if (notice.To is null)
            {
                await chaos.PublishAsync(party.Id, new ChaosEvent(ChaosEvent.Narrator, notice.Text));
            }
            else
            {
                await chaos.SendAsync(party.Id, notice.To, new PrivateMessage(
                    notice.Kind == "ask" ? PrivateKinds.Ask : PrivateKinds.Narrator, notice.Text));
            }
        }
    }
}

// Run-level transitions of the social state.
public sealed class SocialLifecycle(SocialModules modules, AbilityGenerator generator)
{
    // Abilities are dealt once everyone has answered the seed question (or when the host forces the start). Members who
    // join or answer later get theirs on the spot, computed against the same party but never changing anyone else's.
    public bool EnsureAbilities(Party party)
    {
        lock (party.SyncRoot)
        {
            var social = party.Social();
            if (!modules.IsEnabled(SocialModule.Abilities))
            {
                return false;
            }

            var roster = party.Members.Select(m => social.Profile(m.Name)).ToArray();
            var seeded = roster.Where(p => p.Seeded || social.Finalized).ToArray();
            var dealt = generator.GenerateForParty(social.RunSeed, [.. seeded.Select(p => (p.Name, p.Axes))]);
            var changed = false;
            foreach (var profile in seeded.Where(p => p.Abilities.Count == 0))
            {
                profile.Abilities.AddRange(dealt[profile.Name]);
                changed = true;
            }

            return changed;
        }
    }

    // True when this call completed the party's seeding.
    public bool TryFinalize(Party party, bool force)
    {
        lock (party.SyncRoot)
        {
            var social = party.Social();
            if (social.Finalized)
            {
                return false;
            }

            var everyone = party.Members.All(m => social.Profile(m.Name).Seeded);
            if (!everyone && !force)
            {
                return false;
            }

            social.Finalized = true;
            EnsureAbilities(party);
            return true;
        }
    }
}
