using System.Text.RegularExpressions;
using DungeonForge.Application.Features.Social.Asks;
using DungeonForge.Application.Features.Social.Core;

namespace DungeonForge.Application.Features.Social.Supervision;

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
                 new Consequence.Hp(accused, 2),
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
                 new Consequence.Hp(accused, lied ? -3 : 0),
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
