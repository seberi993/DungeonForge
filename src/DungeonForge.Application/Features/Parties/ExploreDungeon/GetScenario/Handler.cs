using DungeonForge.Domain;
using DungeonForge.Domain.Rules;
using DungeonForge.Application.Errors;
using DungeonForge.Application.Narration;

namespace DungeonForge.Application.Features.Parties.ExploreDungeon;

// Query: present (or re-present) the scenario. Never advances the turn, and makes no narrator call when a scenario is already
// stored: only a party's very first turn generates one. While the next scenario is being narrated the party has no Pending
// encounter but an AwaitingRoom; the prompt then answers ScenarioPending (no LLM call, no blocking).
public sealed class GetScenarioHandler(
    IPartyStore parties,
    IRoller roller,
    TurnNarrator turns,
    ClientNarrationCoordinator clientNarration,
    VocabularySelector vocabulary)
{
    // `operation` is the caller's name for errors: LockIn answers the scenario first, under its own operation.
    public async Task<Result<ExploreOutcome>> HandleAsync(Guid id, string operation, CancellationToken ct)
    {
        var claim = Claim(id, operation);
        if (claim.Answer is { } answer)
        {
            return answer;
        }

        var context = claim.Context!;
        if (clientNarration.Enabled)
        {
            return StartClientScenario(id, context, operation);
        }

        // Only a party's first turn gets here, and only the one request that claimed the scenario slot.
        string scenario;
        try
        {
            scenario = await turns.GenerateScenarioAsync(id, context, ct);
        }
        catch
        {
            turns.ReleaseScenarioSlot(id);
            throw;
        }

        if (!parties.TryGet(id, out var party))
        {
            return PartyErrors.NotFound(operation, id);
        }

        var pending = party.CompleteScenario(new PendingEncounter(context.Room, scenario));
        parties.Save(party);
        await turns.BroadcastFirstScenarioAsync(party, pending);
        return new ExploreOutcome(ExploreResponses.Prompt(party, pending));
    }

    // Under the party lock: answer from what is stored, or roll the room and claim the right to generate its scenario.
    private ScenarioClaim Claim(Guid id, string operation)
    {
        if (!parties.TryGet(id, out var party))
        {
            return ScenarioClaim.Answered(PartyErrors.NotFound(operation, id));
        }

        lock (party.SyncRoot)
        {
            if (party.IsPartyDefeated)
            {
                return ScenarioClaim.Answered(ExploreErrors.PartyDefeated(operation, id, party.Turn));
            }

            if (party.Pending is { } existing)
            {
                return ScenarioClaim.Answered(new ExploreOutcome(ExploreResponses.Prompt(party, existing)));
            }

            if (party.AwaitingRoom is { } awaiting)
            {
                return ScenarioClaim.Answered(new ExploreOutcome(ExploreResponses.Awaiting(party, awaiting)));
            }

            if (party.SetupPending)
            {
                return ScenarioClaim.Answered(SetupErrors.Pending(operation, id));
            }

            var room = RoomRules.Roll(roller, party.StoryLean);
            party.TryBeginScenario(room);
            return ScenarioClaim.Claimed(new NarrationContext(room, party.Name, party.Turn, ExploreResponses.IsWounded(party))
            {
                Roster = ExploreResponses.Roster(party),
                Motifs = vocabulary.Pick(),
                SetupAnswers = party.SetupAnswers,
                StoryTags = party.StoryLean.Describe(),
            });
        }
    }

    // The first scenario goes to the player's browser instead of the server narrator.
    private Result<ExploreOutcome> StartClientScenario(Guid id, NarrationContext context, string operation)
    {
        if (!parties.TryGet(id, out var party))
        {
            return PartyErrors.NotFound(operation, id);
        }

        var brief = clientNarration.RegisterScenario(id, party.Turn, context);
        return new ExploreOutcome(ExploreResponses.Awaiting(party, context.Room) with { Brief = brief });
    }
}

// Either the prompt could be answered from what is stored (Answer), or this request now owns generating the scenario (Context).
internal sealed record ScenarioClaim(Result<ExploreOutcome>? Answer, NarrationContext? Context)
{
    public static ScenarioClaim Answered(Result<ExploreOutcome> answer) => new(answer, null);

    public static ScenarioClaim Claimed(NarrationContext context) => new(null, context);
}
