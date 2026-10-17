using System.Collections.Concurrent;
using DungeonForge.Api.Domain;
using DungeonForge.Api.Errors;
using DungeonForge.Api.Features.Parties.Chaos;
using DungeonForge.Api.Features.Parties.Realtime;
using DungeonForge.Api.Features.Parties.SubmitNarration;
using DungeonForge.Api.Features.Scores;
using DungeonForge.Api.Features.Social.Abilities;
using DungeonForge.Api.Infrastructure.Narration;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

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
    ILogger<ExploreDungeonHandler> logger,
    IOptions<NarrationOptions>? options = null,
    IScoreBoard? scores = null,
    ChaosDispatcher? chaos = null,
    VocabularySelector? vocabulary = null,
    SocialTurnPipeline? social = null)
{
    public const string SubmitNarrationOperation = "Command:SubmitNarration";

    private const double WoundedRatio = 0.5;
    private const int RoomKinds = 3;

    // Client narration: while a browser writes the text, the claim waits here until it submits, declines or the deadline hits.
    private sealed record NarrationJob(NarrationContext Context, bool Success, bool IsScenario);

    private readonly ConcurrentDictionary<(Guid PartyId, int Turn), NarrationJob> _jobs = new();

    private bool ClientNarrationOn => options?.Value.ClientNarration == true;

    public Task<Result<ExploreOutcome>> HandleAsync(Guid id, ExploreRequest? request, CancellationToken ct = default)
    {
        social?.RecordIntents(id, request?.Actions, request?.Abilities);
        return request?.Actions is not { Count: > 0 } actions
            ? PromptCoreAsync(id, ExploreOperations.GetScenario, ct)
            : request.SubmitOnly
                ? LockInAsync(id, actions, ct)
                : ResolveAsync(id, actions, waitingIsConflict: true, ExploreOperations.ResolveTurn);
    }

    // ---- Step 1: present (or re-present) the scenario. Never advances the turn. ----

    // Lock-in without the 409: answers the prompt (with lock status) until the last member locks in, which resolves the turn.
    private async Task<Result<ExploreOutcome>> LockInAsync(Guid id, IReadOnlyList<PlayerAction> actions, CancellationToken ct)
    {
        var prompt = await PromptCoreAsync(id, ExploreOperations.LockIn, ct);
        return prompt.IsSuccess && !prompt.Value.Response.ScenarioPending
            ? await ResolveAsync(id, actions, waitingIsConflict: false, ExploreOperations.LockIn)
            : prompt;
    }

    private async Task<Result<ExploreOutcome>> PromptCoreAsync(Guid id, string operation, CancellationToken ct)
    {
        var (early, context) = BeginPrompt(id, operation);
        if (early is { } done)
        {
            return done;
        }

        if (ClientNarrationOn)
        {
            return StartClientScenario(id, context!, operation);
        }

        // Only a party's first turn gets here, and only the one request that claimed the scenario slot.
        string scenario;
        try
        {
            scenario = await GenerateScenarioSafeAsync(id, context!, ct);
        }
        catch
        {
            ReleaseScenarioSlot(id);
            throw;
        }

        if (!cache.TryGetValue(PartyCache.Key(id), out Party? party) || party is null)
        {
            return PartyErrors.NotFound(operation, id);
        }

        var pending = party.CompleteScenario(new PendingEncounter(context!.Room, scenario));
        cache.Set(PartyCache.Key(id), party, PartyCache.EntryOptions());
        await BroadcastFirstScenarioAsync(party, pending);
        return new ExploreOutcome(PromptResponse(party, pending));
    }

    // Other clients that received scenarioPending learn the scenario via NarrationReady (empty outcome = scenario only).
    private Task BroadcastFirstScenarioAsync(Party party, PendingEncounter pending) =>
        BestEffort.RunAsync(logger, ExploreOperations.GetScenario, party.Id, "FirstScenario", async () =>
        {
            var ready = new NarrationReady(party.Id, party.Turn, string.Empty, pending.Scenario, pending.Room);
            await notifier.NarrationReadyAsync(ready, CancellationToken.None);
            await DeliverPrivateAsync(party.Id, pending.Room, party.Turn, []);
        }, party.Turn);

    private (Result<ExploreOutcome>? Early, NarrationContext? Context) BeginPrompt(Guid id, string operation)
    {
        if (!cache.TryGetValue(PartyCache.Key(id), out Party? party) || party is null)
        {
            return (PartyErrors.NotFound(operation, id), null);
        }

        lock (party.SyncRoot)
        {
            if (party.IsPartyDefeated)
            {
                return (ExploreErrors.PartyDefeated(operation, id, party.Turn), null);
            }

            if (party.Pending is { } existing)
            {
                return (new ExploreOutcome(PromptResponse(party, existing)), null);
            }

            if (party.AwaitingRoom is { } awaiting)
            {
                return (new ExploreOutcome(AwaitingResponse(party, awaiting)), null);
            }

            var room = (RoomType)roller.Next(RoomKinds);
            party.TryBeginScenario(room);
            return (null, new NarrationContext(room, party.Name, party.Turn, IsWounded(party))
            {
                Roster = Roster(party),
                Motifs = vocabulary?.Pick() ?? [],
            });
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
            Scenario: pending.Scenario, Discussion: null,
            LockStatus: PartyMapping.ToLockDtos(party.LockStatus));

    private static ExploreResponse AwaitingResponse(Party party, RoomType room) =>
        new(party.Id, party.Turn, room, null, 0, 0, string.Empty, party.Gold, party.IsPartyDefeated, ScenarioPending: true);

    // ---- Step 2: resolve the party's aggregated plan in C# and return the mechanics immediately. ----
    // Phase two (narration + next scenario) runs in the background and is pushed to the party as NarrationReady.

    // Locks the supplied actions in; the turn resolves only once every member is locked, otherwise the party keeps waiting.
    private async Task<Result<ExploreOutcome>> ResolveAsync(
        Guid id, IReadOnlyList<PlayerAction> actions, bool waitingIsConflict, string operation)
    {
        if (!actions.Any(a => NarrationText.Clean(a.Text, ExploreRequestLimits.MaxActionTextLength).Length > 0))
        {
            return ExploreErrors.NoActions(operation, id);
        }

        var (result, context, success, snapshot) = Mutate(id, actions, operation);
        if (result.IsSuccess && result.Value.Waiting is not null && snapshot is not null)
        {
            // Locked answers are private in every mode: peers learn who is ready, never what they wrote.
            if (IsBlind(id))
            {
                await BroadcastReadyAsync(id, actions);
            }

            await BroadcastLockStatusAsync(id, snapshot.LockStatus ?? [], operation, snapshot.Turn);

            return waitingIsConflict ? result : new ExploreOutcome(result.Value.Response);
        }

        if (result.IsFailure || context is null || snapshot is null)
        {
            return result;
        }

        var response = result.Value.Response with { NarrationPending = true };
        await BroadcastAsync(response, snapshot);
        await AnnounceChaosAftermathAsync(response.PartyId);
        if (social is not null) await social.DeliverAsync(response.PartyId);
        if (snapshot.IsPartyDefeated) await RecordScoreAsync(snapshot);

        // The browser of whoever resolved the turn writes the narration; a defeat has no next scenario to wait on,
        // so it keeps the server path.
        if (ClientNarrationOn && context.NextRoom is not null)
        {
            var brief = RegisterClientTurn(response.PartyId, response.Turn, success, context);
            return new ExploreOutcome(response with { Brief = brief });
        }

        _ = Task.Run(() => NarrateAndNotifyAsync(response.PartyId, response.Turn, success, context));
        return new ExploreOutcome(response);
    }

    private bool IsBlind(Guid id) =>
        cache.TryGetValue(PartyCache.Key(id), out Party? party) && party is { Modifiers.Chaos: true };

    private static IReadOnlyList<RosterEntry> Roster(Party party) =>
        [.. party.Members.Select(m => new RosterEntry(m.Name, m.Class))];

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
        if (chaos is null || !cache.TryGetValue(PartyCache.Key(id), out Party? party) || party is null)
        {
            return;
        }

        if (party.Modifiers.Chaos && party.Vault is { } vault)
        {
            await chaos.AnnounceVaultAsync(id, vault);
        }

        await chaos.AnnounceTraitorOutcomeAsync(party, party.EvaluateTraitor());
    }

    // Secrets for the room the party is about to enter and the narrator's whispers; both are private to their targets.
    private async Task DeliverPrivateAsync(Guid partyId, RoomType? nextRoom, int turn, IReadOnlyList<Whisper> whispers)
    {
        if (chaos is null || !cache.TryGetValue(PartyCache.Key(partyId), out Party? party) || party is null)
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

    private async Task NarrateAndNotifyAsync(Guid partyId, int turn, bool success, NarrationContext context)
    {
        TurnNarration narration;
        var source = "server";
        try
        {
            // The narrator has its own timeout and every failure falls back to deterministic flavor.
            narration = await NarrateTurnSafeAsync(partyId, success, context);
            if (narration.FromFallback) source = "fallback";
        }
        catch (Exception ex)
        {
            using var scope = LogScope.Begin(logger, ExploreOperations.NarrateTurn, partyId, turn);
            logger.NarrationPhaseFailed(ex);
            narration = FallbackFlavor.Complete(null, success, context);
            source = "fallback";
        }

        await FinishTurnAsync(partyId, turn, success, context, narration, source);
    }

    // Stores the next scenario (compare-and-set on the turn's claim) BEFORE the push, so a client reacting to NarrationReady
    // can prompt immediately. False when another writer already completed this turn: nothing is pushed twice.
    private async Task<bool> FinishTurnAsync(
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
            source = "fallback";
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

    // Mutates the party object directly (no cache write needed) so a cache fault cannot keep it stuck.
    private void RecoverStuckScenario(Guid partyId, int turn, RoomType? nextRoom, TurnNarration fallback)
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
        if (!cache.TryGetValue(PartyCache.Key(partyId), out Party? party) || party is null)
        {
            return true;
        }

        if (!party.TryCompleteScenario(turn, next))
        {
            return false;
        }

        cache.Set(PartyCache.Key(partyId), party, PartyCache.EntryOptions());
        return true;
    }

    // ---- Client narration: the prompts go to the browser, its text comes back through SubmitClientNarrationAsync. ----

    private Result<ExploreOutcome> StartClientScenario(Guid id, NarrationContext context, string operation)
    {
        if (!cache.TryGetValue(PartyCache.Key(id), out Party? party) || party is null)
        {
            return PartyErrors.NotFound(operation, id);
        }

        var turn = party.Turn;
        _jobs[(id, turn)] = new NarrationJob(context, true, IsScenario: true);
        ScheduleDeadline(id, turn);
        var brief = new NarrationBrief(
            turn, "scenario", NarrationPrompts.ScenarioCompact(context.Room), MasterPromptBuilder.BuildScenario(context),
            null, NarrationPrompts.ScenarioCompactMaxTokens);
        return new ExploreOutcome(AwaitingResponse(party, context.Room) with { Brief = brief });
    }

    private NarrationBrief RegisterClientTurn(Guid partyId, int turn, bool success, NarrationContext context)
    {
        _jobs[(partyId, turn)] = new NarrationJob(context, success, IsScenario: false);
        ScheduleDeadline(partyId, turn);
        return new NarrationBrief(
            turn, "turn", NarrationPrompts.TurnCompact(context.NextRoom), MasterPromptBuilder.Build(success, context),
            NarrationPrompts.TurnJsonSchema(context.NextRoom is not null), NarrationPrompts.TurnCompactMaxTokens);
    }

    // If nobody delivers in time (tab closed, slow GPU), the server narrator takes over exactly once.
    private void ScheduleDeadline(Guid partyId, int turn)
    {
        var delay = TimeSpan.FromSeconds(Math.Max(0, options?.Value.ClientDeadlineSeconds ?? 40));
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
            await NarrateAndNotifyAsync(partyId, turn, job.Success, job.Context);
            return;
        }

        var scenario = await GenerateScenarioSafeAsync(partyId, job.Context, CancellationToken.None);
        await CompleteClientScenarioAsync(partyId, turn, job.Context.Room, scenario);
    }

    private async Task<bool> CompleteClientScenarioAsync(Guid partyId, int turn, RoomType room, string scenario)
    {
        if (!cache.TryGetValue(PartyCache.Key(partyId), out Party? party) || party is null)
        {
            return false;
        }

        var pending = new PendingEncounter(room, scenario);
        if (!party.TryCompleteScenario(turn, pending))
        {
            return false;
        }

        cache.Set(PartyCache.Key(partyId), party, PartyCache.EntryOptions());
        await BroadcastFirstScenarioAsync(party, pending);
        return true;
    }

    // Accepts the browser's model output for the claimed turn. Blank text or a declined flag hands over to the server
    // narrator at once; unparseable turn JSON does the same instead of canning the whole turn.
    public async Task<Result> SubmitClientNarrationAsync(Guid partyId, int turn, string? text, bool declined)
    {
        if (!cache.TryGetValue(PartyCache.Key(partyId), out Party? party) || party is null)
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
            return await CompleteClientScenarioAsync(partyId, turn, job.Context.Room, scenario)
                ? Result.Success
                : NarrationErrors.NotAwaited(SubmitNarrationOperation, partyId, turn);
        }

        var narration = FallbackFlavor.Complete(partial, job.Success, job.Context);
        return await FinishTurnAsync(partyId, turn, job.Success, job.Context, narration, narration.FromFallback ? "fallback" : "model")
            ? Result.Success
            : NarrationErrors.NotAwaited(SubmitNarrationOperation, partyId, turn);
    }

    // Outside the lock; failures are logged, never thrown.
    private Task BroadcastLockStatusAsync(Guid id, IReadOnlyList<LockStatusDto> status, string operation, int turn) =>
        BestEffort.RunAsync(logger, operation, id, "TurnLockUpdated", () => notifier.TurnLockUpdatedAsync(id, status, CancellationToken.None), turn);

    private static CovertIntent? CleanCovert(CovertIntent? covert) =>
        covert is null ? null : covert with { Target = NarrationText.Clean(covert.Target, ExploreRequestLimits.MaxActionNameLength) };

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
            party.TryLockIn(new PlayerAction(action.MemberId, name, text, CleanCovert(action.Covert)));
        }
    }

    private (Result<ExploreOutcome> Result, NarrationContext? Context, bool Success, GetPartyStateResponse? Snapshot) Mutate(
        Guid id, IReadOnlyList<PlayerAction> actions, string operation)
    {
        var key = PartyCache.Key(id);
        if (!cache.TryGetValue(key, out Party? party) || party is null)
        {
            return (PartyErrors.NotFound(operation, id), null, false, null);
        }

        // Per-party lock: serializes mutation against Party.Snapshot() readers and concurrent resolves.
        lock (party.SyncRoot)
        {
            if (party.IsPartyDefeated)
            {
                return (ExploreErrors.PartyDefeated(operation, id, party.Turn), null, false, null);
            }

            party.TryResolveConfrontation(roller);
            if (party.Confrontation is not null)
            {
                return (ExploreErrors.ConfrontationInProgress(operation, id, party.Turn), null, false, null);
            }

            if (party.Pending is null)
            {
                return (party.AwaitingRoom is null
                    ? ExploreErrors.NoActiveEncounter(operation, id, party.Turn)
                    : ExploreErrors.ScenarioPending(operation, id, party.Turn), null, false, null);
            }

            SubmitAll(party, actions);
            var pending = party.Pending!;
            if (pending.Discussion is not { Count: > 0 } discussion)
            {
                return (ExploreErrors.NoActions(operation, id), null, false, null);
            }

            if (!party.AllLockedIn)
            {
                cache.Set(key, party, PartyCache.EntryOptions());
                var waiting = new WaitingForPartyResponse(
                    party.Id, party.LockedCount, party.RequiredLocks, PartyMapping.ToLockDtos(party.LockStatus));
                return (new ExploreOutcome(PromptResponse(party, pending), waiting),
                    null, false, PartyMapping.ToResponse(party.Snapshot()));
            }

            var turnBefore = party.Turn;
            var floorBefore = party.Floor;
            // Every locked-in action rolls on its own, with odds shifted by what the rest of the party locked in.
            var chances = ActionOdds.For(discussion);

            // Hidden gifts nudge the same numbers (chance, failure damage, gold); they never replace the rolls.
            var plan = social?.Plan(party, discussion, chances, turnBefore);
            if (plan is not null)
            {
                chances = [.. Enumerable.Range(0, chances.Count).Select(plan.Chance)];
            }

            // Secret plans resolve first, in C#; a sabotaged spell fails no matter how well the roll goes.
            var covert = discussion.Any(a => a.Covert is not null)
                ? CovertResolver.Resolve(discussion, party.Members.Where(m => !party.IsBanished(m.Name)), roller)
                : CovertResolution.None;
            var failed = new List<DamageResult>();
            var passed = 0;
            var crits = 0;
            var bossDamage = 0;
            var rolls = new List<RollDto>();
            for (var i = 0; i < discussion.Count; i++)
            {
                var roll = roller.Next(100);
                var crit = ActionOdds.IsCrit(roll);
                var fumble = ActionOdds.IsFumble(roll);
                rolls.Add(new RollDto(discussion[i].Name, roll, chances[i], crit, fumble));
                if (roll < chances[i] && !covert.Sabotaged.Contains(discussion[i].Name))
                {
                    passed++;
                    if (crit) crits++;
                    bossDamage += crit ? ActionOdds.BossCritDamage : ActionOdds.BossHitDamage;
                    continue;
                }

                var damage = FailDamage(pending.Room, turnBefore, floorBefore);
                if (plan is not null) damage = plan.Reduce(i, damage);
                failed.Add(new DamageResult(discussion[i].Name, fumble ? ActionOdds.FumbleDamage(damage) : damage));
            }

            // The room itself needs a strict majority; a tie goes to the dungeon.
            var success = passed > failed.Count;
            var isBoss = pending.Room == RoomType.Boss;
            var bossFalls = isBoss && party.BossHp - bossDamage <= 0;
            success |= bossFalls;
            var paid = isBoss ? bossFalls : success;
            var gold = paid ? Gold(pending.Room, turnBefore, floorBefore) + crits * ActionOdds.CritGold + (plan?.GoldBonus ?? 0) : 0;

            // Chaos parties do not bank drops: gold and the boss's Cursed Idol wait in the Unclaimed Vault.
            var contested = party.Modifiers.Chaos;
            var hits = party.ApplyMemberOutcomes([.. failed, .. covert.Hits], contested ? 0 : gold);
            foreach (var (thief, amount) in covert.Heals) party.Heal(thief, amount);
            if (contested && (gold > 0 || bossFalls)) party.DropVault(gold, bossFalls ? TraitorPact.CursedIdol : null);
            var hit = hits.Count > 0 ? hits[0] : null;

            // The next room is rolled in C# before any narrator call; nothing follows a defeated party. A boss
            // that is awake (or just woken by the floor's last room) is always the next room.
            RoomType? nextRoom = null;
            var floorCleared = false;
            if (!party.IsPartyDefeated)
            {
                var progress = party.AdvanceProgress(pending.Room, bossDamage);
                floorCleared = progress.FloorCleared;
                nextRoom = progress.BossNext ? RoomType.Boss : (RoomType)roller.Next(RoomKinds);
                party.TryBeginScenario(nextRoom.Value);
            }

            // Mutated in place under the lock; re-Set refreshes the sliding expiry.
            cache.Set(key, party, PartyCache.EntryOptions());

            // Touch the invite entry so its sliding expiry keeps pace with an active party.
            if (party.InviteCode.Length > 0) cache.TryGetValue(PartyCache.InviteKey(party.InviteCode), out _);

            var response = new ExploreResponse(
                party.Id, party.Turn, pending.Room, hit?.TargetName, hit?.Damage ?? 0, gold,
                pending.Scenario, party.Gold, party.IsPartyDefeated, Discussion: discussion,
                Beats: ToBeats(party, discussion), Success: success,
                Hits: [.. hits.Select(h => new HitDto(h.TargetName, h.Damage))], Rolls: rolls,
                Floor: party.Floor, BossHp: party.BossHp, BossMaxHp: party.BossMaxHp, FloorCleared: floorCleared,
                CombatLog: covert.Outcomes.Count > 0 ? covert.Log : null);
            var context = new NarrationContext(pending.Room, party.Name, turnBefore, IsWounded(party))
            {
                Floor = floorBefore,
                FloorCleared = floorCleared,
                Actions = discussion,
                NextRoom = nextRoom,
                Failed = [.. failed.Select(f => f.TargetName)],
                Downed = [.. party.Members.Where(m => m.IsDefeated).Select(m => m.Name)],
                Roster = Roster(party),
                Motifs = vocabulary?.Pick() ?? [],
                Treachery = covert.Log,
            };

            return (new ExploreOutcome(response), context, success, PartyMapping.ToResponse(party.Snapshot()));
        }
    }

    private static ActionBeat[] ToBeats(Party party, IReadOnlyList<PlayerAction> discussion) =>
        [.. discussion.Select(action =>
        {
            var characterClass = party.Members.FirstOrDefault(m => m.Name == action.Name)?.Class ?? CharacterClass.Warrior;
            return new ActionBeat(action.Name, characterClass, ActionVerbs.Pick(characterClass, action.Text), action.Text);
        })];

    // Game math lives here, never in the narrator.
    // Each member whose own action failed takes this hit; it grows as the dungeon deepens.
    // The boss hits harder per floor and does not scale with the turn count, so a long floor stays fair.
    private static int FailDamage(RoomType room, int turn, int floor) => room switch
    {
        RoomType.Combat => 6 + turn % 3 + turn / 3,
        RoomType.Puzzle => 7 + turn % 3 + turn / 3,
        RoomType.Boss => 8 + 2 * (floor - 1) + turn % 3,
        _ => 4 + turn / 3,
    };

    // Boss gold is only paid when the boss falls.
    private static int Gold(RoomType room, int turn, int floor) => room switch
    {
        RoomType.Chest => 5 + turn,
        RoomType.Puzzle => 10 + 2 * turn,
        RoomType.Boss => 25 * floor,
        _ => 0,
    };

    private static bool IsWounded(Party party) =>
        party.Members.Sum(m => m.Hp) <= party.Members.Sum(m => m.MaxHp) * WoundedRatio;

    // ---- Narration with deterministic fallbacks: a misbehaving narrator never fails the request. ----

    private async Task<string> GenerateScenarioSafeAsync(Guid partyId, NarrationContext context, CancellationToken ct)
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

    // Outside the lock; a broadcast failure must never fail the explore request.
    // CancellationToken.None: an actor disconnect must not drop the broadcast to the rest of the party.
    // Separate try/catch per message so one failure does not skip the other.
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
}
