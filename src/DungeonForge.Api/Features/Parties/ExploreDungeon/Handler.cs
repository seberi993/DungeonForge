using DungeonForge.Api.Domain;
using DungeonForge.Api.Features.Parties.Realtime;
using DungeonForge.Api.Infrastructure.Narration;
using Microsoft.Extensions.Caching.Memory;

namespace DungeonForge.Api.Features.Parties.ExploreDungeon;

public sealed class ExploreDungeonHandler(
    IMemoryCache cache,
    IRoller roller,
    IDungeonNarrator narrator,
    IPartyNotifier notifier,
    ILogger<ExploreDungeonHandler> logger)
{
    private const double WoundedRatio = 0.5;
    private const int ChoiceCount = 3;
    private const int RoomKinds = 3;
    private const int ActionBonusPercent = 5;
    private const int MaxActionBonusPercent = 15;

    // Success chance (percent) per choice index, safest to riskiest. Success when roller.Next(100) < chance.
    private static readonly int[] SuccessChance = [80, 55, 30];

    public Task<ExploreResult> HandleAsync(Guid id, ExploreRequest? request, CancellationToken ct = default) =>
        request?.SelectedChoiceIndex is { } index
            ? ResolveAsync(id, index, request.Actions, ct)
            : PromptAsync(id, request?.Actions, ct);

    // ---- Step 1: present (or re-present) the dilemma. Never advances the turn. ----

    private async Task<ExploreResult> PromptAsync(Guid id, IReadOnlyList<PlayerAction>? actions, CancellationToken ct)
    {
        var result = await PromptCoreAsync(id, ct);
        return actions is { Count: > 0 } && result.Status == ExploreStatus.Explored
            ? await SubmitActionsAsync(id, actions) ?? result
            : result;
    }

    private async Task<ExploreResult> PromptCoreAsync(Guid id, CancellationToken ct)
    {
        var (early, room, context) = BeginPrompt(id);
        if (early is { } done)
        {
            return done;
        }

        // Narration is awaited outside the lock; it never influences game state.
        var choice = await GenerateChoicesSafeAsync(context!, ct);

        var party = cache.Get<Party>(PartyCache.Key(id));
        if (party is null)
        {
            return new ExploreResult(ExploreStatus.NotFound);
        }

        // First writer wins: concurrent prompt requests all see the same encounter.
        var pending = party.TrySetPending(new PendingEncounter(room!.Value, choice.Prompt, choice.Options.ToArray()));
        cache.Set(PartyCache.Key(id), party, PartyCache.EntryOptions());
        return new ExploreResult(ExploreStatus.Explored, PromptResponse(party, pending));
    }

    private (ExploreResult? Early, RoomType? Room, NarrationContext? Context) BeginPrompt(Guid id)
    {
        if (!cache.TryGetValue(PartyCache.Key(id), out Party? party) || party is null)
        {
            return (new ExploreResult(ExploreStatus.NotFound), null, null);
        }

        lock (party.SyncRoot)
        {
            if (party.IsPartyDefeated)
            {
                return (new ExploreResult(ExploreStatus.Defeated), null, null);
            }

            if (party.Pending is { } existing)
            {
                return (new ExploreResult(ExploreStatus.Explored, PromptResponse(party, existing)), null, null);
            }

            var room = (RoomType)roller.Next(RoomKinds);
            return (null, room, new NarrationContext(room, party.Name, party.Turn, IsWounded(party)));
        }
    }

    private static ExploreResponse PromptResponse(Party party, PendingEncounter pending) =>
        new(party.Id, party.Turn, pending.Room, null, 0, 0, pending.Prompt, party.Gold, party.IsPartyDefeated,
            ChoicePrompt: pending.Prompt, Choices: pending.Choices, Discussion: pending.Discussion);

    // ---- Step 2: resolve the chosen option in C# and return the mechanics immediately. ----
    // Phase two (narration) runs in the background and is pushed to the party as NarrationReady.

    private async Task<ExploreResult> ResolveAsync(Guid id, int index, IReadOnlyList<PlayerAction>? actions, CancellationToken ct)
    {
        var (result, context, option, success, snapshot) = Mutate(id, index, actions);
        if (result.Response is not { } response || context is null || option is null || snapshot is null)
        {
            return result;
        }

        response = response with { NarrationPending = true };
        await BroadcastAsync(response, snapshot);

        _ = Task.Run(() => NarrateAndNotifyAsync(response.PartyId, response.Turn, option, success, context));
        return result with { Response = response };
    }

    private async Task NarrateAndNotifyAsync(Guid partyId, int turn, string option, bool success, NarrationContext context)
    {
        // Never throws: the narrator has its own timeout and every failure falls back to deterministic flavor.
        var outcome = await NarrateOutcomeSafeAsync(option, success, context, CancellationToken.None);
        try
        {
            await notifier.NarrationReadyAsync(new NarrationReady(partyId, turn, outcome), CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to broadcast NarrationReady for turn {Turn} of party {PartyId}", turn, partyId);
        }
    }

    // Submits tactical input to the pending encounter without resolving it. Null when the party/encounter vanished.
    private async Task<ExploreResult?> SubmitActionsAsync(Guid id, IReadOnlyList<PlayerAction> actions)
    {
        if (!cache.TryGetValue(PartyCache.Key(id), out Party? party) || party is null)
        {
            return null;
        }

        ExploreResponse response;
        IReadOnlyList<PlayerAction> discussion;
        lock (party.SyncRoot)
        {
            SubmitAll(party, actions);
            if (party.Pending is not { } pending)
            {
                return null;
            }

            discussion = pending.Discussion ?? [];
            response = PromptResponse(party, pending);
        }

        cache.Set(PartyCache.Key(id), party, PartyCache.EntryOptions());

        try
        {
            await notifier.DiscussionUpdatedAsync(id, discussion, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to broadcast DiscussionUpdated for party {PartyId}", id);
        }

        return new ExploreResult(ExploreStatus.Explored, response);
    }

    private static void SubmitAll(Party party, IReadOnlyList<PlayerAction>? actions)
    {
        foreach (var action in actions ?? [])
        {
            var text = NarrationText.Clean(action.Text, ExploreRequestLimits.MaxActionTextLength);
            if (text.Length == 0)
            {
                continue;
            }

            var name = NarrationText.Sanitize(action.Name, ExploreRequestLimits.MaxActionNameLength, "Adventurer");
            party.TrySubmitAction(new PlayerAction(action.MemberId, name, text));
        }
    }

    private (ExploreResult Result, NarrationContext? Context, string? Option, bool Success, GetPartyStateResponse? Snapshot) Mutate(
        Guid id, int index, IReadOnlyList<PlayerAction>? actions)
    {
        var key = PartyCache.Key(id);
        if (!cache.TryGetValue(key, out Party? party) || party is null)
        {
            return (new ExploreResult(ExploreStatus.NotFound), null, null, false, null);
        }

        // Per-party lock: serializes mutation against Party.Snapshot() readers and concurrent choices.
        lock (party.SyncRoot)
        {
            if (party.IsPartyDefeated)
            {
                return (new ExploreResult(ExploreStatus.Defeated), null, null, false, null);
            }

            if (index is < 0 or >= ChoiceCount)
            {
                return (Invalid("Choice index must be between 0 and 2."), null, null, false, null);
            }

            if (party.Pending is not { } pending)
            {
                return (Invalid("There is no active encounter to resolve."), null, null, false, null);
            }

            SubmitAll(party, actions);
            pending = party.Pending!;

            var turnBefore = party.Turn;
            var wounded = IsWounded(party);
            var bonus = Math.Min(MaxActionBonusPercent, party.ActionBonusCount * ActionBonusPercent);
            var success = roller.Next(100) < SuccessChance[index] + bonus;
            var (damage, gold) = Deltas(pending.Room, success, turnBefore);

            var hit = party.ApplyChoiceOutcome(damage, gold);

            // Mutated in place under the lock; re-Set refreshes the sliding expiry.
            cache.Set(key, party, PartyCache.EntryOptions());

            // Touch the invite entry so its sliding expiry keeps pace with an active party.
            if (party.InviteCode.Length > 0) cache.TryGetValue(PartyCache.InviteKey(party.InviteCode), out _);

            var response = new ExploreResponse(
                party.Id, party.Turn, pending.Room, hit?.TargetName, hit?.Damage ?? 0, gold,
                pending.Prompt, party.Gold, party.IsPartyDefeated, Discussion: pending.Discussion);
            var context = new NarrationContext(pending.Room, party.Name, turnBefore, wounded)
            {
                Actions = pending.Discussion ?? [],
            };

            return (new ExploreResult(ExploreStatus.Explored, response), context, pending.Choices[index], success,
                PartyMapping.ToResponse(party.Snapshot()));
        }
    }

    private static ExploreResult Invalid(string error) => new(ExploreStatus.InvalidChoice, Error: error);

    // Game math lives here, never in the narrator.
    private static (int Damage, int Gold) Deltas(RoomType room, bool success, int turn) => (room, success) switch
    {
        (RoomType.Combat, false) => (2 + turn % 3, 0),
        (RoomType.Chest, true) => (0, 5 + turn),
        (RoomType.Puzzle, true) => (0, 10 + 2 * turn),
        (RoomType.Puzzle, false) => (4 + turn % 3, 0),
        _ => (0, 0),
    };

    private static bool IsWounded(Party party) =>
        party.Members.Sum(m => m.Hp) <= party.Members.Sum(m => m.MaxHp) * WoundedRatio;

    // ---- Narration with deterministic fallbacks: a misbehaving narrator never fails the request. ----

    private async Task<EncounterChoice> GenerateChoicesSafeAsync(NarrationContext context, CancellationToken ct)
    {
        try
        {
            var choice = await narrator.GenerateChoicesAsync(context, ct);
            if (IsValid(choice))
            {
                return choice;
            }

            logger.LogWarning("Narrator returned an invalid encounter for turn {Turn}; using fallback.", context.Turn);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Narrator failed generating choices for turn {Turn}; using fallback.", context.Turn);
        }

        return FallbackFlavor.Choices(context.Room, context.Turn);
    }

    private static bool IsValid(EncounterChoice? choice) =>
        choice is { Options: { Count: ChoiceCount } options }
        && !string.IsNullOrWhiteSpace(choice.Prompt)
        && options.All(o => !string.IsNullOrWhiteSpace(o));

    private async Task<string> NarrateOutcomeSafeAsync(string option, bool success, NarrationContext context, CancellationToken ct)
    {
        try
        {
            var text = await narrator.NarrateOutcomeAsync(option, success, context, ct);
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text;
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Narrator failed narrating outcome for turn {Turn}; using fallback.", context.Turn);
        }

        return FallbackFlavor.Outcome(context.Room, success, context.Turn);
    }

    // Outside the lock; a broadcast failure must never fail the explore request.
    // CancellationToken.None: an actor disconnect must not drop the broadcast to the rest of the party.
    // Separate try/catch per message so one failure does not skip the other.
    private async Task BroadcastAsync(ExploreResponse response, GetPartyStateResponse snapshot)
    {
        try
        {
            await notifier.TurnResolvedAsync(response, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to broadcast TurnResolved for turn {Turn} of party {PartyId}", response.Turn, response.PartyId);
        }

        try
        {
            await notifier.PartyUpdatedAsync(snapshot, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to broadcast PartyUpdated for turn {Turn} of party {PartyId}", response.Turn, response.PartyId);
        }
    }
}
