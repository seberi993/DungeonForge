using System.Text.RegularExpressions;
using DungeonForge.Api.Domain;
using DungeonForge.Api.Features.Social.Asks;
using DungeonForge.Api.Features.Social.Core;

namespace DungeonForge.Api.Features.Social.Supervision;

// Read-only view the rules judge against. Rules never mutate; they return a Decision and ConsequenceApplier acts on it.
public sealed class SocialContext(Party party, DateTimeOffset now)
{
    public DateTimeOffset Now { get; } = now;

    public int Floor => party.Floor;

    public PlayerProfile? Profile(string name) => party.Social().FindProfile(name);

    public int PocketedGold(string name) => party.PocketedGold(name);
}

// One adjudication rule. Register or remove rules in DI to change how the narrator judges; none knows about the others.
// The first rule (by Order) that returns a Decision for an event decides it.
public interface ISocialRule
{
    string Id { get; }

    int Order { get; }

    Decision? Evaluate(SocialContext context, SocialEvent socialEvent);
}

public interface ISocialSupervisor
{
    Decision Judge(SocialContext context, SocialEvent socialEvent);
}

public sealed class RuleSupervisor(IEnumerable<ISocialRule> rules) : ISocialSupervisor
{
    private readonly ISocialRule[] _rules = [.. rules.OrderBy(r => r.Order)];

    public Decision Judge(SocialContext context, SocialEvent socialEvent)
    {
        foreach (var rule in _rules)
        {
            if (rule.Evaluate(context, socialEvent) is { } decision)
            {
                return decision;
            }
        }

        return new Decision(socialEvent.Id, Verdict.Ignore, 0, [], "R-NONE", "No rule applies; logged only.");
    }
}

// Applies a Decision to the party. Bounds live here, so no rule (and no model output) can exceed them.
public static class ConsequenceApplier
{
    public const int MaxAxisShift = 8;
    public const int MaxReputationShift = 2;
    public const int MaxTokenShift = 2;
    public const int MaxNoticeLength = 140;

    public const int MaxHpShift = 6;
    public const int MaxGoldShift = 10;

    // Social consequences plus the ones that touch the dungeon itself. The party lock is re-entrant, so callers that
    // already hold it can call this directly.
    public static void Apply(Party party, Decision decision)
    {
        lock (party.SyncRoot)
        {
            var social = party.Social();
            Apply(social, decision);
            foreach (var consequence in decision.Consequences)
            {
                switch (consequence)
                {
                    case Consequence.Hp hp when party.Members.FirstOrDefault(m => string.Equals(m.Name, hp.Player, StringComparison.OrdinalIgnoreCase)) is { } member:
                        var delta = Math.Clamp(hp.Delta, -MaxHpShift, MaxHpShift);
                        if (delta < 0) party.DamageMember(member.Name, -delta);
                        else if (delta > 0) party.Heal(member.Name, delta);
                        social.StateChanged = true;
                        break;
                    case Consequence.Gold gold:
                        party.AdjustGold(Math.Clamp(gold.Delta, -MaxGoldShift, MaxGoldShift));
                        social.StateChanged = true;
                        break;
                }
            }
        }
    }

    // Social part only (axes, reputation, credibility, tokens, notices); game-state consequences need a Party.
    public static void Apply(PartySocial social, Decision decision)
    {
        foreach (var consequence in decision.Consequences)
        {
            switch (consequence)
            {
                case Consequence.AxisShift shift when social.FindProfile(shift.Player) is { } p:
                    p.Axes.Add(shift.Axis, Math.Clamp(shift.Delta, -MaxAxisShift, MaxAxisShift));
                    break;
                case Consequence.Reputation rep when social.FindProfile(rep.Player) is { } p:
                    p.Trust += Math.Clamp(rep.Trust, -MaxReputationShift, MaxReputationShift);
                    p.Menace += Math.Clamp(rep.Menace, -MaxReputationShift, MaxReputationShift);
                    break;
                case Consequence.Credibility cred when social.FindProfile(cred.Player) is { } p:
                    p.Credibility = Math.Clamp(p.Credibility + Math.Clamp(cred.Delta, -1, 1), 0, PlayerProfile.MaxCredibility);
                    break;
                case Consequence.Tokens tokens when social.FindProfile(tokens.Player) is { } p:
                    p.AskTokens = Math.Max(0, p.AskTokens + Math.Clamp(tokens.Delta, -MaxTokenShift, MaxTokenShift));
                    break;
                case Consequence.Notice notice:
                    var text = notice.Message.Text.Length > MaxNoticeLength ? notice.Message.Text[..MaxNoticeLength] : notice.Message.Text;
                    social.Outbox.Add(notice.Message with { Text = text });
                    break;
            }
        }
    }
}

// Records the event, asks the supervisor, applies the outcome and keeps the audit row. With the Supervision module off the
// event is still journaled (Ignore) so debugging data survives, but nothing is judged or applied.
public sealed class SocialPipeline(
    ISocialSupervisor supervisor, SocialModules modules, TimeProvider time, NarratorJudgeQueue? judgeQueue = null)
{
    public Decision Process(Party party, SocialEvent socialEvent)
    {
        lock (party.SyncRoot)
        {
            var social = party.Social();
            var now = time.GetUtcNow();

            // Everyone the event names is a real member (handlers validated them), so give them a profile to act on.
            foreach (var name in socialEvent.Actors.Concat(socialEvent.Data.Where(d => d.Key == "from").Select(d => d.Value)))
            {
                social.Profile(name);
            }

            var decision = modules.IsEnabled(SocialModule.Supervision)
                ? supervisor.Judge(new SocialContext(party, now), socialEvent)
                : new Decision(socialEvent.Id, Verdict.Ignore, 0, [], "R-OFF", "Supervision module is off.");
            ConsequenceApplier.Apply(party, decision);
            social.Record(new AuditEntry(socialEvent, decision, SeededRng.Derive(social.RunSeed, "event:" + socialEvent.Id), now));
            social.Count(SocialMetrics.ForEvent(socialEvent.Type));
            if (decision.Verdict != Verdict.Ignore) social.Count(SocialMetrics.ForVerdict(decision.Verdict));
            if (decision.Verdict == Verdict.Unresolved)
            {
                judgeQueue?.TryEnqueue(party, socialEvent);
            }

            return decision;
        }
    }

    public SocialEvent NewEvent(Party party, string type, int turn, IEnumerable<string> actors, IDictionary<string, string> data, params string[] evidence)
    {
        lock (party.SyncRoot)
        {
            return new SocialEvent(party.Social().NextId("ne"), type, turn, [.. actors], new Dictionary<string, string>(data), evidence);
        }
    }
}
