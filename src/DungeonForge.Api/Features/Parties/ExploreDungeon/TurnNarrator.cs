using DungeonForge.Api.Domain;
using DungeonForge.Api.Errors;
using DungeonForge.Api.Features.Parties.Chaos;
using DungeonForge.Api.Features.Parties.Realtime;
using DungeonForge.Api.Infrastructure.Narration;

namespace DungeonForge.Api.Features.Parties.ExploreDungeon;

// The server side of narration: the narrator call with its deterministic fallbacks, storing the next scenario, and pushing
// NarrationReady plus the private words that follow it. A misbehaving narrator never fails a request: every failure here
// falls back to canned text and leaves a coded log event.
public sealed class TurnNarrator(
    IPartyStore parties,
    IDungeonNarrator narrator,
    IPartyNotifier notifier,
    ChaosDispatcher chaos,
    ILogger<TurnNarrator> logger)
{
    public async Task<string> GenerateScenarioAsync(Guid partyId, NarrationContext context, CancellationToken ct)
    {
        try
        {
            var text = await narrator.GenerateScenarioAsync(context, ct);
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text;
            }

            using var scope = LogScope.Begin(logger, ExploreOperations.GetScenario, partyId, context.Turn);
            logger.NarratorEmptyScenario();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            using var scope = LogScope.Begin(logger, ExploreOperations.GetScenario, partyId, context.Turn);
            logger.NarratorScenarioFailed(ex);
        }

        return FallbackFlavor.Scenario(context.Room, context.Turn);
    }

    // Other clients that received scenarioPending learn the scenario via NarrationReady (empty outcome = scenario only).
    public Task BroadcastFirstScenarioAsync(Party party, PendingEncounter pending) =>
        BestEffort.RunAsync(logger, ExploreOperations.GetScenario, party.Id, "FirstScenario", async () =>
        {
            var ready = new NarrationReady(party.Id, party.Turn, string.Empty, pending.Scenario, pending.Room);
            await notifier.NarrationReadyAsync(ready, CancellationToken.None);
            await DeliverPrivateAsync(party.Id, pending.Room, party.Turn, []);
        }, party.Turn);

    // Frees the claim on the next scenario when generating it was abandoned (the party must never stay AwaitingRoom).
    public void ReleaseScenarioSlot(Guid id)
    {
        if (parties.TryGet(id, out var party))
        {
            party.AbortScenario();
        }
    }

    public async Task NarrateAndNotifyAsync(Guid partyId, int turn, bool success, NarrationContext context)
    {
        TurnNarration narration;
        var source = NarrationSources.Server;
        try
        {
            // The narrator has its own timeout and every failure falls back to deterministic flavor.
            narration = await NarrateTurnSafeAsync(partyId, success, context);
            if (narration.FromFallback) source = NarrationSources.Fallback;
        }
        catch (Exception ex)
        {
            using var scope = LogScope.Begin(logger, ExploreOperations.NarrateTurn, partyId, turn);
            logger.NarrationPhaseFailed(ex);
            narration = FallbackFlavor.Complete(null, success, context);
            source = NarrationSources.Fallback;
        }

        await FinishTurnAsync(partyId, turn, success, context, narration, source);
    }

    // Stores the next scenario (compare-and-set on the turn's claim) BEFORE the push, so a client reacting to NarrationReady
    // can prompt immediately. False when another writer already completed this turn: nothing is pushed twice.
    public async Task<bool> FinishTurnAsync(
        Guid partyId, int turn, bool success, NarrationContext context, TurnNarration narration, string source)
    {
        // Outer guard: whatever fails, the party must never be left with an AwaitingRoom and no scenario.
        try
        {
            if (context.NextRoom is { } nextRoom
                && !TryStoreScenario(partyId, turn, new PendingEncounter(nextRoom, narration.Next)))
            {
                return false;
            }
        }
        catch (Exception ex)
        {
            using (LogScope.Begin(logger, ExploreOperations.NarrateTurn, partyId, turn))
            {
                logger.NarrationPhaseFailed(ex);
            }

            narration = FallbackFlavor.Complete(null, success, context);
            source = NarrationSources.Fallback;
            RecoverStuckScenario(partyId, turn, context.NextRoom, narration);
        }

        var ready = new NarrationReady(
            partyId, turn, narration.Outcome, context.NextRoom is null ? null : narration.Next, context.NextRoom,
            narration.NextScene.Length == 0 ? null : narration.NextScene, source);
        await BestEffort.RunAsync(
            logger, ExploreOperations.NarrateTurn, partyId, "NarrationReady",
            () => notifier.NarrationReadyAsync(ready, CancellationToken.None), turn);

        await DeliverPrivateAsync(partyId, context.NextRoom, turn + 1, narration.Whispers);
        return true;
    }

    private async Task<TurnNarration> NarrateTurnSafeAsync(Guid partyId, bool success, NarrationContext context)
    {
        try
        {
            var narration = await narrator.NarrateTurnAsync(success, context, CancellationToken.None);
            return FallbackFlavor.Complete(narration, success, context);
        }
        catch (Exception ex)
        {
            using var scope = LogScope.Begin(logger, ExploreOperations.NarrateTurn, partyId, context.Turn);
            logger.NarratorTurnFailed(ex);
            return FallbackFlavor.Complete(null, success, context);
        }
    }

    // Secrets for the room the party is about to enter and the narrator's whispers; both are private to their targets.
    private async Task DeliverPrivateAsync(Guid partyId, RoomType? nextRoom, int turn, IReadOnlyList<Whisper> whispers)
    {
        if (!parties.TryGet(partyId, out var party))
        {
            return;
        }

        await chaos.DispatchWhispersAsync(party, whispers);
        await chaos.DispatchGaslightingAsync(party);
        if (nextRoom is { } room)
        {
            await chaos.DispatchRoomSecretsAsync(party, room, turn);
        }
    }

    // Mutates the party object directly (no cache write needed) so a cache fault cannot keep it stuck.
    private void RecoverStuckScenario(Guid partyId, int turn, RoomType? nextRoom, TurnNarration fallback)
    {
        try
        {
            if (nextRoom is null || !parties.TryGet(partyId, out var party))
            {
                return;
            }

            lock (party.SyncRoot)
            {
                if (party.AwaitingRoom is not null)
                {
                    party.TryCompleteScenario(turn, new PendingEncounter(nextRoom.Value, fallback.Next));
                }
            }
        }
        catch (Exception ex)
        {
            using (LogScope.Begin(logger, ExploreOperations.NarrateTurn, partyId, turn))
            {
                logger.FallbackScenarioFailed(ex);
            }

            ReleaseScenarioSlotSafe(partyId, turn);
        }
    }

    private void ReleaseScenarioSlotSafe(Guid partyId, int turn)
    {
        try
        {
            ReleaseScenarioSlot(partyId);
        }
        catch (Exception ex)
        {
            using var scope = LogScope.Begin(logger, ExploreOperations.NarrateTurn, partyId, turn);
            logger.ScenarioSlotReleaseFailed(ex);
        }
    }

    // A party that left the cache has nothing to complete, so that counts as stored (the push still goes out).
    private bool TryStoreScenario(Guid partyId, int turn, PendingEncounter next)
    {
        if (!parties.TryGet(partyId, out var party))
        {
            return true;
        }

        if (!party.TryCompleteScenario(turn, next))
        {
            return false;
        }

        parties.Save(party);
        return true;
    }
}
