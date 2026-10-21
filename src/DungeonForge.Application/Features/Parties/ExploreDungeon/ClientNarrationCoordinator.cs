using System.Collections.Concurrent;
using DungeonForge.Domain;
using DungeonForge.Application.Errors;
using DungeonForge.Application.Features.Parties.SubmitNarration;
using DungeonForge.Application.Narration;
using Microsoft.Extensions.Options;

namespace DungeonForge.Application.Features.Parties.ExploreDungeon;

// Client narration: the prompts go to the player's browser and its text comes back through SubmitAsync. While a browser
// writes the text, the claim waits here until it submits, declines or the deadline hits, and then the server narrator takes
// over exactly once. The job table is the only state, and removing an entry is the atomic claim between submit and deadline.
public sealed class ClientNarrationCoordinator(
    IPartyStore parties,
    TurnNarrator turns,
    IOptions<NarrationOptions> options,
    ILogger<ClientNarrationCoordinator> logger)
{
    public const string SubmitNarrationOperation = "Command:SubmitNarration";

    private sealed record NarrationJob(NarrationContext Context, bool Success, bool IsScenario);

    private readonly ConcurrentDictionary<(Guid PartyId, int Turn), NarrationJob> _jobs = new();

    public bool Enabled => options.Value.ClientNarration;

    // Claims the party's first scenario for the browser and returns what its local model should run.
    public NarrationBrief RegisterScenario(Guid partyId, int turn, NarrationContext context)
    {
        _jobs[(partyId, turn)] = new NarrationJob(context, true, IsScenario: true);
        ScheduleDeadline(partyId, turn);
        return new NarrationBrief(
            turn, NarrationKinds.Scenario, NarrationPrompts.ScenarioCompact(context.Room), MasterPromptBuilder.BuildScenario(context),
            null, NarrationPrompts.ScenarioCompactMaxTokens);
    }

    // Claims a resolved turn's narration for the browser of whoever resolved it.
    public NarrationBrief RegisterTurn(Guid partyId, int turn, bool success, NarrationContext context)
    {
        _jobs[(partyId, turn)] = new NarrationJob(context, success, IsScenario: false);
        ScheduleDeadline(partyId, turn);
        return new NarrationBrief(
            turn, NarrationKinds.Turn, NarrationPrompts.TurnCompact(context.NextRoom), MasterPromptBuilder.Build(success, context),
            NarrationPrompts.TurnJsonSchema(context.NextRoom is not null), NarrationPrompts.TurnCompactMaxTokens);
    }

    // Accepts the browser's model output for the claimed turn. Blank text or a declined flag hands over to the server
    // narrator at once; unparseable turn JSON does the same instead of canning the whole turn.
    public async Task<Result> SubmitAsync(Guid partyId, int turn, string? text, bool declined)
    {
        if (!parties.TryGet(partyId, out _))
        {
            return PartyErrors.NotFound(SubmitNarrationOperation, partyId);
        }

        // Atomic claim: the deadline task and this call race on the same entry, only one can take it.
        if (!_jobs.TryRemove((partyId, turn), out var job))
        {
            return NarrationErrors.NotAwaited(SubmitNarrationOperation, partyId, turn);
        }

        var cleaned = declined ? string.Empty : text?.Trim() ?? string.Empty;
        TurnNarration? partial = job.IsScenario || cleaned.Length == 0 ? null : TurnNarrationParser.Parse(cleaned);
        var scenario = job.IsScenario ? NarrationText.Clean(cleaned, TurnNarrationParser.MaxScenarioLength) : string.Empty;
        if (job.IsScenario ? scenario.Length == 0 : partial is null)
        {
            _ = Task.Run(() => RunServerNarrationAsync(partyId, turn, job));
            return Result.Success;
        }

        if (job.IsScenario)
        {
            return await CompleteScenarioAsync(partyId, turn, job.Context.Room, scenario)
                ? Result.Success
                : NarrationErrors.NotAwaited(SubmitNarrationOperation, partyId, turn);
        }

        var narration = FallbackFlavor.Complete(partial, job.Success, job.Context);
        return await turns.FinishTurnAsync(
                partyId, turn, job.Success, job.Context, narration, narration.FromFallback ? NarrationSources.Fallback : NarrationSources.Model)
            ? Result.Success
            : NarrationErrors.NotAwaited(SubmitNarrationOperation, partyId, turn);
    }

    // If nobody delivers in time (tab closed, slow GPU), the server narrator takes over exactly once.
    private void ScheduleDeadline(Guid partyId, int turn)
    {
        var delay = TimeSpan.FromSeconds(Math.Max(0, options.Value.ClientDeadlineSeconds));
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(delay);
                if (_jobs.TryRemove((partyId, turn), out var job))
                {
                    await RunServerNarrationAsync(partyId, turn, job);
                }
            }
            catch (Exception ex)
            {
                using var scope = LogScope.Begin(logger, ExploreOperations.ClientNarrationDeadline, partyId, turn);
                logger.DeadlineNarrationFailed(ex);
            }
        });
    }

    private async Task RunServerNarrationAsync(Guid partyId, int turn, NarrationJob job)
    {
        if (!job.IsScenario)
        {
            await turns.NarrateAndNotifyAsync(partyId, turn, job.Success, job.Context);
            return;
        }

        var scenario = await turns.GenerateScenarioAsync(partyId, job.Context, CancellationToken.None);
        await CompleteScenarioAsync(partyId, turn, job.Context.Room, scenario);
    }

    private async Task<bool> CompleteScenarioAsync(Guid partyId, int turn, RoomType room, string scenario)
    {
        if (!parties.TryGet(partyId, out var party))
        {
            return false;
        }

        var pending = new PendingEncounter(room, scenario);
        if (!party.TryCompleteScenario(turn, pending))
        {
            return false;
        }

        parties.Save(party);
        await turns.BroadcastFirstScenarioAsync(party, pending);
        return true;
    }
}
