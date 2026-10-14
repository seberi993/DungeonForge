using DungeonForge.Api.Domain;
using DungeonForge.Api.Features.Parties.Realtime;
using DungeonForge.Api.Infrastructure.Narration;
using Microsoft.Extensions.Caching.Memory;

namespace DungeonForge.Api.Features.Parties.ExploreDungeon;

// One LLM call per turn, strictly in C#-owned order:
//   prompt  : pending scenario stored -> return it, NO narrator call. Only a party's very first turn generates one.
//   resolve : roll success + next room in C#, apply deltas, return mechanics immediately, then ONE narrator call
//             (outcome + next scenario) in the background; the scenario is stored before NarrationReady is pushed.
// In-flight window (design decision): while a scenario is being narrated the party has no Pending encounter but an
// AwaitingRoom. A prompt then returns ScenarioPending (no LLM call, no blocking; the text arrives via NarrationReady and
// the next prompt) and a resolve is rejected, so a turn can neither race a second narrator call nor be resolved twice.
public sealed class ExploreDungeonHandler(
    IMemoryCache cache,
    IRoller roller,
    IDungeonNarrator narrator,
    IPartyNotifier notifier,
    ILogger<ExploreDungeonHandler> logger)
{
    private const double WoundedRatio = 0.5;
    private const int RoomKinds = 3;
    private const int BaseSuccessPercent = 55;
    private const int ActionBonusPercent = 5;
    private const int MaxActionBonusPercent = 15;

    public Task<ExploreResult> HandleAsync(Guid id, ExploreRequest? request, CancellationToken ct = default) =>
        request?.Actions is not { Count: > 0 } actions
            ? PromptCoreAsync(id, ct)
            : request.SubmitOnly
                ? LockInAsync(id, actions, ct)
                : ResolveAsync(id, actions, waitingIsConflict: true);

    // ---- Step 1: present (or re-present) the scenario. Never advances the turn. ----

    // Lock-in without the 409: answers the prompt (with lock status) until the last member locks in, which resolves the turn.
    private async Task<ExploreResult> LockInAsync(Guid id, IReadOnlyList<PlayerAction> actions, CancellationToken ct)
    {
        var prompt = await PromptCoreAsync(id, ct);
        return prompt is { Status: ExploreStatus.Explored, Response.ScenarioPending: false }
            ? await ResolveAsync(id, actions, waitingIsConflict: false)
            : prompt;
    }

    private async Task<ExploreResult> PromptCoreAsync(Guid id, CancellationToken ct)
    {
        var (early, context) = BeginPrompt(id);
        if (early is { } done)
        {
            return done;
        }

        // Only a party's first turn gets here, and only the one request that claimed the scenario slot.
        string scenario;
        try
        {
            scenario = await GenerateScenarioSafeAsync(context!, ct);
        }
        catch
        {
            ReleaseScenarioSlot(id);
            throw;
        }

        if (!cache.TryGetValue(PartyCache.Key(id), out Party? party) || party is null)
        {
            return new ExploreResult(ExploreStatus.NotFound);
        }

        var pending = party.CompleteScenario(new PendingEncounter(context!.Room, scenario));
        cache.Set(PartyCache.Key(id), party, PartyCache.EntryOptions());
        await BroadcastFirstScenarioAsync(party, pending);
        return new ExploreResult(ExploreStatus.Explored, PromptResponse(party, pending));
    }

    // Other clients that received scenarioPending learn the scenario via NarrationReady (empty outcome = scenario only).
    private async Task BroadcastFirstScenarioAsync(Party party, PendingEncounter pending)
    {
        try
        {
            var ready = new NarrationReady(party.Id, party.Turn, string.Empty, pending.Scenario, pending.Room);
            await notifier.NarrationReadyAsync(ready, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to broadcast first scenario for party {PartyId}", party.Id);
        }
    }

    private (ExploreResult? Early, NarrationContext? Context) BeginPrompt(Guid id)
    {
        if (!cache.TryGetValue(PartyCache.Key(id), out Party? party) || party is null)
        {
            return (new ExploreResult(ExploreStatus.NotFound), null);
        }

        lock (party.SyncRoot)
        {
            if (party.IsPartyDefeated)
            {
                return (new ExploreResult(ExploreStatus.Defeated), null);
            }

            if (party.Pending is { } existing)
            {
                return (new ExploreResult(ExploreStatus.Explored, PromptResponse(party, existing)), null);
            }

            if (party.AwaitingRoom is { } awaiting)
            {
                return (new ExploreResult(ExploreStatus.Explored, AwaitingResponse(party, awaiting)), null);
            }

            var room = (RoomType)roller.Next(RoomKinds);
            party.TryBeginScenario(room);
            return (null, new NarrationContext(room, party.Name, party.Turn, IsWounded(party)));
        }
    }

    private void ReleaseScenarioSlot(Guid id)
    {
        if (cache.TryGetValue(PartyCache.Key(id), out Party? party) && party is not null)
        {
            party.AbortScenario();
        }
    }

    private static ExploreResponse PromptResponse(Party party, PendingEncounter pending) =>
        new(party.Id, party.Turn, pending.Room, null, 0, 0, pending.Scenario, party.Gold, party.IsPartyDefeated,
            Scenario: pending.Scenario, Discussion: pending.Discussion,
            LockStatus: PartyMapping.ToLockDtos(party.LockStatus));

    private static ExploreResponse AwaitingResponse(Party party, RoomType room) =>
        new(party.Id, party.Turn, room, null, 0, 0, string.Empty, party.Gold, party.IsPartyDefeated, ScenarioPending: true);

    // ---- Step 2: resolve the party's aggregated plan in C# and return the mechanics immediately. ----
    // Phase two (narration + next scenario) runs in the background and is pushed to the party as NarrationReady.

    // Locks the supplied actions in; the turn resolves only once every member is locked, otherwise the party keeps waiting.
    private async Task<ExploreResult> ResolveAsync(Guid id, IReadOnlyList<PlayerAction> actions, bool waitingIsConflict)
    {
        if (!actions.Any(a => NarrationText.Clean(a.Text, ExploreRequestLimits.MaxActionTextLength).Length > 0))
        {
            return Invalid("At least one non-empty action is required.");
        }

        var (result, context, success, snapshot) = Mutate(id, actions);
        if (result.Status == ExploreStatus.Waiting && result.Response is { } waiting && snapshot is not null)
        {
            await BroadcastLockAsync(id, waiting.Discussion ?? [], snapshot.LockStatus ?? []);
            return waitingIsConflict ? result : result with { Status = ExploreStatus.Explored, Waiting = null };
        }

        if (result.Response is not { } response || context is null || snapshot is null)
        {
            return result;
        }

        response = response with { NarrationPending = true };
        await BroadcastAsync(response, snapshot);

        _ = Task.Run(() => NarrateAndNotifyAsync(response.PartyId, response.Turn, success, context));
        return result with { Response = response };
    }

    private async Task NarrateAndNotifyAsync(Guid partyId, int turn, bool success, NarrationContext context)
    {
        // Outer guard: whatever fails, the party must never be left with an AwaitingRoom and no scenario.
        TurnNarration narration;
        try
        {
            // The narrator has its own timeout and every failure falls back to deterministic flavor.
            narration = await NarrateTurnSafeAsync(success, context);

            // The next scenario is stored BEFORE the push so a client reacting to NarrationReady can prompt immediately.
            if (context.NextRoom is { } nextRoom)
            {
                StoreNextScenario(partyId, new PendingEncounter(nextRoom, narration.Next));
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Narration phase failed for turn {Turn} of party {PartyId}; completing with fallback.", turn, partyId);
            narration = FallbackFlavor.Complete(null, success, context);
            RecoverStuckScenario(partyId, context.NextRoom, narration);
        }

        try
        {
            var ready = new NarrationReady(
                partyId, turn, narration.Outcome, context.NextRoom is null ? null : narration.Next, context.NextRoom);
            await notifier.NarrationReadyAsync(ready, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to broadcast NarrationReady for turn {Turn} of party {PartyId}", turn, partyId);
        }
    }

    // Mutates the party object directly (no cache write needed) so a cache fault cannot keep it stuck.
    private void RecoverStuckScenario(Guid partyId, RoomType? nextRoom, TurnNarration fallback)
    {
        try
        {
            if (nextRoom is null || !cache.TryGetValue(PartyCache.Key(partyId), out Party? party) || party is null)
            {
                return;
            }

            lock (party.SyncRoot)
            {
                if (party.AwaitingRoom is not null)
                {
                    party.CompleteScenario(new PendingEncounter(nextRoom.Value, fallback.Next));
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Fallback scenario failed for party {PartyId}; aborting scenario slot.", partyId);
            ReleaseScenarioSlotSafe(partyId);
        }
    }

    private void ReleaseScenarioSlotSafe(Guid partyId)
    {
        try
        {
            ReleaseScenarioSlot(partyId);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not release scenario slot for party {PartyId}", partyId);
        }
    }

    private void StoreNextScenario(Guid partyId, PendingEncounter next)
    {
        if (cache.TryGetValue(PartyCache.Key(partyId), out Party? party) && party is not null)
        {
            party.CompleteScenario(next);
            cache.Set(PartyCache.Key(partyId), party, PartyCache.EntryOptions());
        }
    }

    // Outside the lock; failures are logged, never thrown.
    private async Task BroadcastLockAsync(Guid id, IReadOnlyList<PlayerAction> discussion, IReadOnlyList<LockStatusDto> status)
    {
        try
        {
            await notifier.DiscussionUpdatedAsync(id, discussion, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to broadcast DiscussionUpdated for party {PartyId}", id);
        }

        await BroadcastLockStatusAsync(id, status);
    }

    private async Task BroadcastLockStatusAsync(Guid id, IReadOnlyList<LockStatusDto> status)
    {
        try
        {
            await notifier.TurnLockUpdatedAsync(id, status, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to broadcast TurnLockUpdated for party {PartyId}", id);
        }
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
            party.TryLockIn(new PlayerAction(action.MemberId, name, text));
        }
    }

    private (ExploreResult Result, NarrationContext? Context, bool Success, GetPartyStateResponse? Snapshot) Mutate(
        Guid id, IReadOnlyList<PlayerAction> actions)
    {
        var key = PartyCache.Key(id);
        if (!cache.TryGetValue(key, out Party? party) || party is null)
        {
            return (new ExploreResult(ExploreStatus.NotFound), null, false, null);
        }

        // Per-party lock: serializes mutation against Party.Snapshot() readers and concurrent resolves.
        lock (party.SyncRoot)
        {
            if (party.IsPartyDefeated)
            {
                return (new ExploreResult(ExploreStatus.Defeated), null, false, null);
            }

            if (party.Pending is null)
            {
                return (Invalid(party.AwaitingRoom is null
                    ? "There is no active encounter to resolve."
                    : "The next scenario is still being written; wait for it before acting."), null, false, null);
            }

            SubmitAll(party, actions);
            var pending = party.Pending!;
            if (pending.Discussion is not { Count: > 0 } discussion)
            {
                return (Invalid("At least one non-empty action is required."), null, false, null);
            }

            if (!party.AllLockedIn)
            {
                cache.Set(key, party, PartyCache.EntryOptions());
                var waiting = new WaitingForPartyResponse(
                    party.Id, party.LockedCount, party.RequiredLocks, PartyMapping.ToLockDtos(party.LockStatus));
                return (new ExploreResult(ExploreStatus.Waiting, PromptResponse(party, pending), Waiting: waiting),
                    null, false, PartyMapping.ToResponse(party.Snapshot()));
            }

            var turnBefore = party.Turn;
            var bonus = Math.Min(MaxActionBonusPercent, party.ActionBonusCount * ActionBonusPercent);
            var success = roller.Next(100) < BaseSuccessPercent + bonus;
            var (damage, gold) = Deltas(pending.Room, success, turnBefore);

            var hit = party.ApplyChoiceOutcome(damage, gold);

            // The next room is rolled in C# before any narrator call; nothing follows a defeated party.
            RoomType? nextRoom = null;
            if (!party.IsPartyDefeated)
            {
                nextRoom = (RoomType)roller.Next(RoomKinds);
                party.TryBeginScenario(nextRoom.Value);
            }

            // Mutated in place under the lock; re-Set refreshes the sliding expiry.
            cache.Set(key, party, PartyCache.EntryOptions());

            // Touch the invite entry so its sliding expiry keeps pace with an active party.
            if (party.InviteCode.Length > 0) cache.TryGetValue(PartyCache.InviteKey(party.InviteCode), out _);

            var response = new ExploreResponse(
                party.Id, party.Turn, pending.Room, hit?.TargetName, hit?.Damage ?? 0, gold,
                pending.Scenario, party.Gold, party.IsPartyDefeated, Discussion: discussion);
            var context = new NarrationContext(pending.Room, party.Name, turnBefore, IsWounded(party))
            {
                Actions = discussion,
                NextRoom = nextRoom,
            };

            return (new ExploreResult(ExploreStatus.Explored, response), context, success, PartyMapping.ToResponse(party.Snapshot()));
        }
    }

    private static ExploreResult Invalid(string error) => new(ExploreStatus.InvalidRequest, Error: error);

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

    private async Task<string> GenerateScenarioSafeAsync(NarrationContext context, CancellationToken ct)
    {
        try
        {
            var text = await narrator.GenerateScenarioAsync(context, ct);
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text;
            }

            logger.LogWarning("Narrator returned an empty scenario for turn {Turn}; using fallback.", context.Turn);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Narrator failed generating the scenario for turn {Turn}; using fallback.", context.Turn);
        }

        return FallbackFlavor.Scenario(context.Room, context.Turn);
    }

    private async Task<TurnNarration> NarrateTurnSafeAsync(bool success, NarrationContext context)
    {
        try
        {
            var narration = await narrator.NarrateTurnAsync(success, context, CancellationToken.None);
            return FallbackFlavor.Complete(narration, success, context);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Narrator failed narrating turn {Turn}; using fallback.", context.Turn);
            return FallbackFlavor.Complete(null, success, context);
        }
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

        // The resolve cleared every lock.
        await BroadcastLockStatusAsync(response.PartyId, snapshot.LockStatus ?? []);
    }
}
