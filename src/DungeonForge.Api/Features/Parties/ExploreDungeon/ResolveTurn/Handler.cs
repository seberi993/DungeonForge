using DungeonForge.Api.Domain;
using DungeonForge.Api.Errors;
using DungeonForge.Api.Features.Parties.Chaos;
using DungeonForge.Api.Features.Parties.Realtime;
using DungeonForge.Api.Features.Scores;
using DungeonForge.Api.Features.Social.Abilities;
using DungeonForge.Api.Infrastructure.Narration;

namespace DungeonForge.Api.Features.Parties.ExploreDungeon;

// Command: lock the supplied actions in; the turn resolves only once every member is locked, otherwise the answer is the
// waiting variant (the prompt state plus who is missing). A resolved turn answers with its mechanics at once, tells the party,
// and hands the narration (the next scenario's text) to the server narrator or to the resolver's browser. Exactly one narrator
// call happens per turn, and the next scenario is stored before NarrationReady is pushed.
public sealed class ResolveTurnHandler(
    IPartyStore parties,
    TurnResolver resolver,
    TurnNarrator turns,
    ClientNarrationCoordinator clientNarration,
    IPartyNotifier notifier,
    ILogger<ResolveTurnHandler> logger,
    IScoreBoard? scores = null,
    ChaosDispatcher? chaos = null,
    SocialTurnPipeline? social = null)
{
    // `operation` is the caller's name for errors: LockIn resolves through here under its own operation.
    public async Task<Result<ExploreOutcome>> HandleAsync(Guid id, IReadOnlyList<PlayerAction> actions, string operation)
    {
        if (!actions.Any(a => NarrationText.Clean(a.Text, ExploreRequestLimits.MaxActionTextLength).Length > 0))
        {
            return ExploreErrors.NoActions(operation, id);
        }

        var resolution = resolver.Resolve(id, actions, operation);
        if (resolution.IsWaiting)
        {
            // Locked answers are private in every mode: peers learn who is ready, never what they wrote.
            if (IsBlind(id))
            {
                await BroadcastReadyAsync(id, actions);
            }

            await BroadcastLockStatusAsync(id, resolution.Snapshot!.LockStatus ?? [], operation, resolution.Snapshot.Turn);
            return resolution.Outcome;
        }

        if (!resolution.IsResolved)
        {
            return resolution.Outcome;
        }

        var (context, success, snapshot) = (resolution.Context!, resolution.Success, resolution.Snapshot!);
        var response = resolution.Outcome.Value.Response with { NarrationPending = true };
        await BroadcastAsync(response, snapshot);
        await AnnounceChaosAftermathAsync(response.PartyId);
        if (social is not null) await social.DeliverAsync(response.PartyId);
        if (snapshot.IsPartyDefeated) await RecordScoreAsync(snapshot);

        // The browser of whoever resolved the turn writes the narration; a defeat has no next scenario to wait on,
        // so it keeps the server path.
        if (clientNarration.Enabled && context.NextRoom is not null)
        {
            var brief = clientNarration.RegisterTurn(response.PartyId, response.Turn, success, context);
            return new ExploreOutcome(response with { Brief = brief });
        }

        _ = Task.Run(() => turns.NarrateAndNotifyAsync(response.PartyId, response.Turn, success, context));
        return new ExploreOutcome(response);
    }

    private bool IsBlind(Guid id) =>
        parties.TryGet(id, out var party) && party is { Modifiers.Chaos: true };

    private async Task BroadcastReadyAsync(Guid id, IReadOnlyList<PlayerAction> actions)
    {
        if (chaos is null) return;
        foreach (var action in actions.Where(a => NarrationText.Clean(a.Text, ExploreRequestLimits.MaxActionTextLength).Length > 0))
        {
            var name = NarrationText.Sanitize(action.Name, ExploreRequestLimits.MaxActionNameLength, "Adventurer");
            await chaos.AnnouncePlayerReadyAsync(id, name);
        }
    }

    // After the Master Turn resolved: the new unclaimed vault is public, and a traitor win ends the run with a reveal.
    private async Task AnnounceChaosAftermathAsync(Guid id)
    {
        if (chaos is null || !parties.TryGet(id, out var party))
        {
            return;
        }

        if (party.Modifiers.Chaos && party.Vault is { } vault)
        {
            await chaos.AnnounceVaultAsync(id, vault);
        }

        await chaos.AnnounceTraitorOutcomeAsync(party, party.EvaluateTraitor());
    }

    // Only the resolve that wipes the party reaches here (later resolves answer Defeated), so each run is recorded once.
    // A board outage never blocks the turn.
    private async Task RecordScoreAsync(GetPartyStateResponse party)
    {
        if (scores is null) return;
        try
        {
            await scores.RecordAsync(new RunScore(
                party.PartyId, party.Name, [.. party.Members.Select(m => m.Name)],
                party.Floor, party.Turn, party.Gold, DateTimeOffset.UtcNow));
        }
        catch (Exception ex)
        {
            using var scope = LogScope.Begin(logger, ExploreOperations.ResolveTurn, party.PartyId, party.Turn);
            logger.ScoreRecordFailed(ex);
        }
    }

    // Outside the lock; a broadcast failure must never fail the explore request.
    // CancellationToken.None: an actor disconnect must not drop the broadcast to the rest of the party.
    // Separate sends per message so one failure does not skip the other.
    private async Task BroadcastAsync(ExploreResponse response, GetPartyStateResponse snapshot)
    {
        await BestEffort.RunAsync(
            logger, ExploreOperations.ResolveTurn, response.PartyId, "TurnResolved",
            () => notifier.TurnResolvedAsync(response, CancellationToken.None), response.Turn);
        await BestEffort.RunAsync(
            logger, ExploreOperations.ResolveTurn, response.PartyId, "PartyUpdated",
            () => notifier.PartyUpdatedAsync(snapshot, CancellationToken.None), response.Turn);

        // The resolve cleared every lock.
        await BroadcastLockStatusAsync(response.PartyId, snapshot.LockStatus ?? [], ExploreOperations.ResolveTurn, response.Turn);
    }

    private Task BroadcastLockStatusAsync(Guid id, IReadOnlyList<LockStatusDto> status, string operation, int turn) =>
        BestEffort.RunAsync(logger, operation, id, "TurnLockUpdated", () => notifier.TurnLockUpdatedAsync(id, status, CancellationToken.None), turn);
}
