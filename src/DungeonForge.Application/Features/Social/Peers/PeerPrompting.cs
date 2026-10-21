using DungeonForge.Domain;
using DungeonForge.Application.Features.Social.Core;

namespace DungeonForge.Application.Features.Social.Peers;

// Chooses which teammate question a player sees next. Caller holds Party.SyncRoot.
public static class PeerPrompting
{
    public static int Answered(PartySocial social, string rater) =>
        social.PeerRounds.Count(r => r.Responses.ContainsKey(rater));

    public static int Allowance(Party party, PeerParams p) => p.StartingPrompts + (party.Floor - 1);

    // Idempotent: asking again before answering returns the same round. Null when nobody is left to rate or the allowance
    // for this floor is spent.
    public static PeerRound? NextFor(Party party, string rater, PeerParams p)
    {
        var social = party.Social();
        if (social.FindProfile(rater) is { OptedOut: true })
        {
            return null;
        }

        if (social.PeerAssignments.TryGetValue(rater, out var currentId)
            && social.PeerRounds.FirstOrDefault(r => r.Id == currentId) is { Resolved: false } current
            && !current.Responses.ContainsKey(rater))
        {
            return current;
        }

        social.PeerAssignments.Remove(rater);
        if (Answered(social, rater) >= Allowance(party, p))
        {
            return null;
        }

        // Prefer finishing a round somebody already started (most answers first), so effects land sooner.
        var open = social.PeerRounds
            .Where(r => !r.Resolved && !Same(r.About, rater) && !r.Responses.ContainsKey(rater))
            .OrderByDescending(r => r.Responses.Count)
            .ThenBy(r => r.Id, StringComparer.Ordinal)
            .FirstOrDefault();
        var round = open ?? Open(party, rater);
        if (round is not null)
        {
            social.PeerAssignments[rater] = round.Id;
        }

        return round;
    }

    private static PeerRound? Open(Party party, string rater)
    {
        var social = party.Social();
        var subjects = party.Members
            .Select(m => m.Name)
            .Where(n => !Same(n, rater) && social.FindProfile(n) is not { OptedOut: true })
            .ToArray();
        if (subjects.Length == 0)
        {
            return null;
        }

        var counter = social.PeerRounds.Count;
        var rng = SeededRng.For(social.RunSeed, $"peer-open:{rater}", counter);

        // Spread attention: the teammate with the fewest rounds so far, ties broken by the seed.
        var fewest = subjects.Min(s => social.PeerRounds.Count(r => Same(r.About, s)));
        var pool = subjects.Where(s => social.PeerRounds.Count(r => Same(r.About, s)) == fewest).ToArray();
        var about = pool[rng.Next(pool.Length)];

        var unused = PeerQuestions.All.Where(q => !social.PeerRounds.Any(r => Same(r.About, about) && r.QuestionId == q.Id)).ToArray();
        var questions = unused.Length > 0 ? unused : [.. PeerQuestions.All];
        var question = questions[rng.Next(questions.Length)];

        var round = new PeerRound(social.NextId("pr"), about, question.Id);
        social.PeerRounds.Add(round);
        return round;
    }

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
