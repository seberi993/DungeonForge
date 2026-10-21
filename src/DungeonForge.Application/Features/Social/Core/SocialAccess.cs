using DungeonForge.Domain;
using DungeonForge.Application.Errors;
using DungeonForge.Application.Features.Parties;
using DungeonForge.Application.Features.Parties.Chaos;
using DungeonForge.Application.Features.Parties.Realtime;
using DungeonForge.Application.Features.Social.Abilities;

namespace DungeonForge.Application.Features.Social.Core;

public sealed record SocialPlayer(Party Party, PartyMember Member);

// What every social slice does first: find the party, check the module is on, resolve the named player.
public sealed class SocialAccess(IPartyStore parties, SocialModules modules)
{
    // A null module means "any part of the social game", used by the read-only overview.
    public Result<SocialPlayer> Resolve(Guid partyId, string? playerName, SocialModule? module, string operation)
    {
        if (!parties.TryGet(partyId, out var party))
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
    public void Touch(Party party) => parties.Save(party);
}

// Pushes what the lock-protected code queued: the narrator's party-wide lines and private words. Failures are logged by
// ChaosDispatcher (BestEffort) and never fail the request.
public sealed class SocialDelivery(ChaosDispatcher chaos, IPartyNotifier notifier, ILogger<SocialDelivery> logger)
{
    public async Task DeliverAsync(Party party)
    {
        IReadOnlyList<SocialNotice> notices;
        GetPartyStateResponse? state = null;
        lock (party.SyncRoot)
        {
            var social = party.Social();
            notices = social.DrainOutbox();

            // A penalty or reward moved HP or gold: everyone's party panel needs the new numbers.
            if (social.StateChanged)
            {
                social.StateChanged = false;
                state = PartyMapping.ToResponse(party.Snapshot());
            }
        }

        if (state is not null)
        {
            await BestEffort.RunAsync(
                logger, "Social:Deliver", party.Id, "PartyUpdated", () => notifier.PartyUpdatedAsync(state, CancellationToken.None));
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

// A fresh whisper-shard for every living member each time the party reaches a new floor.
public static class ShardRefill
{
    public const int MaxTokens = 5;

    // Idempotent per floor; safe to call whenever the party lock is held or not (it takes the re-entrant lock itself).
    public static void Apply(Party party)
    {
        lock (party.SyncRoot)
        {
            var social = party.Social();
            var floors = party.Floor - social.RefilledFloor;
            if (floors <= 0)
            {
                return;
            }

            social.RefilledFloor = party.Floor;
            foreach (var member in party.Members.Where(m => !m.IsDefeated))
            {
                var profile = social.Profile(member.Name);
                profile.AskTokens = Math.Min(MaxTokens, Math.Max(profile.AskTokens, 0) + floors);
                social.Outbox.Add(new SocialNotice(member.Name, "narrator", "A fresh whisper-shard forms in your palm."));
            }
        }
    }
}
