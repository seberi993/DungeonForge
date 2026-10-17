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
            ConsequenceApplier.Apply(social, decision);
            social.Record(new AuditEntry(socialEvent, decision, SeededRng.Derive(social.RunSeed, "event:" + socialEvent.Id), now));
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

// ---- Rules ----

public sealed class UnanimousPeerRule : ISocialRule
{
    public string Id => "R-UNANIMOUS";

    public int Order => 10;

    public Decision? Evaluate(SocialContext context, SocialEvent e)
    {
        if (e.Type != SocialEventTypes.PeerUnanimous)
        {
            return null;
        }

        var subject = e.Actors[0];
        var positive = e.Data.GetValueOrDefault("direction") == "yes";
        return positive
            ? new Decision(e.Id, Verdict.Celebrate, 1,
                [new Consequence.Reputation(subject, 1, 0),
                 new Consequence.Notice(new SocialNotice(subject, "narrator", "All eyes agree on you. Something stirs."))],
                Id, "Every teammate who answered vouched for the same strength.")
            : new Decision(e.Id, Verdict.Punish, 1,
                [new Consequence.Reputation(subject, -1, 0),
                 new Consequence.Notice(new SocialNotice(subject, "narrator", "The party's doubt settles on your shoulders."))],
                Id, "Every teammate who answered doubted the same thing.");
    }
}

// An accusation about stolen loot, judged against what the dungeon actually knows (the pocketed gold) and how the accused
// answered. Clear evidence is rewarded or punished; murky evidence stays unresolved and earns, at most, a hint.
public sealed partial class TheftClaimRule : ISocialRule
{
    public string Id => "R-THEFT";

    public int Order => 20;

    [GeneratedRegex(@"\b(gold|loot|vault|coin|coins|steal|stole|stolen|took|take|pocket|pocketed|theft|thief)\b", RegexOptions.IgnoreCase)]
    private static partial Regex TheftWords();

    public Decision? Evaluate(SocialContext context, SocialEvent e)
    {
        if (e.Type != SocialEventTypes.AskReply || e.Data.GetValueOrDefault("kind") != nameof(AskKind.Accusation)
            || !TheftWords().IsMatch(e.Data.GetValueOrDefault("text") ?? string.Empty))
        {
            return null;
        }

        var accuser = e.Data["from"];
        var accused = e.Actors[0];
        var choice = Enum.Parse<AskChoice>(e.Data["choice"]);
        var evidence = context.PocketedGold(accused) > 0 ? 1 : 0;
        evidence += context.Profile(accused) is { Menace: > 0 } ? 1 : 0;
        var guilty = context.PocketedGold(accused) > 0;

        if (guilty && choice == AskChoice.Honest)
        {
            return new Decision(e.Id, Verdict.Celebrate, 1,
                [new Consequence.Reputation(accused, 1, 0),
                 new Consequence.Credibility(accuser, 1),
                 new Consequence.Notice(new SocialNotice(accuser, "narrator", "The dark nods: you were right."))],
                Id, "Gold is pocketed and the accused owned up; honesty is rewarded and the accuser vindicated.");
        }

        if (guilty)
        {
            var lied = choice == AskChoice.Bluff;
            return new Decision(e.Id, Verdict.Punish, lied ? 2 : 1,
                [new Consequence.Reputation(accused, -1, lied ? 1 : 0),
                 new Consequence.Credibility(accused, lied ? -1 : 0),
                 new Consequence.Credibility(accuser, 1),
                 new Consequence.Notice(new SocialNotice(accuser, "narrator", "A lie curdles in the air. You were right."))],
                Id, lied
                    ? "Gold is pocketed and the accused bluffed; punished, accuser vindicated."
                    : "Gold is pocketed and the accused dodged; mild penalty, accuser vindicated.");
        }

        if (choice == AskChoice.Honest && evidence == 0)
        {
            return new Decision(e.Id, Verdict.Punish, 1,
                [new Consequence.Reputation(accuser, -1, 0),
                 new Consequence.Credibility(accuser, -1),
                 new Consequence.Notice(new SocialNotice(accuser, "narrator", "The dark does not like unfounded claims."))],
                Id, "Nothing is pocketed, no history against the accused, and they answered plainly; the claim was false.");
        }

        return new Decision(e.Id, Verdict.Unresolved, 1,
            [new Consequence.Notice(new SocialNotice(accuser, "narrator", "The answer is hard to read. Nothing is proven."))],
            Id, "No corroborating loot, but the accused dodged or bluffed; ambiguous, left unresolved.");
    }
}

public sealed class SubgroupConsensusRule : ISocialRule
{
    public string Id => "R-CONSENSUS";

    public int Order => 30;

    public Decision? Evaluate(SocialContext context, SocialEvent e) =>
        e.Type == SocialEventTypes.AskConsensus
            ? new Decision(e.Id, Verdict.Celebrate, 1,
                [new Consequence.Notice(new SocialNotice(e.Data["from"], "narrator", "They stand together."))],
                Id, "Everyone asked answered the same way; the asker learns the group agrees.")
            : null;
}
