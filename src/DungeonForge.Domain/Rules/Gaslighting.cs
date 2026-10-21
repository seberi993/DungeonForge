using System.Globalization;

namespace DungeonForge.Domain.Rules;

public sealed record Gaslight(string Target, string Text);

// Psychological warfare: every chaos turn each living member gets a private claim about the rest of the party. Rules live in
// C#; most claims are deliberately false (invented thefts, a framed "traitor", fake wounds) so players cannot trust the narrator.
public static class Gaslighting
{
    private const int LieChancePercent = 70;

    private static readonly string[] Thefts =
    [
        "I watched {0} slip {1} gold into their coat when the spoils were split. Say nothing. Watch.",
        "{0} is carrying {1} more gold than they declared. Funny, how the count never matches.",
        "Count your coins, then count {0}'s. Someone took {1} gold, and it was not the dungeon.",
    ];

    private static readonly string[] Wounds =
    [
        "{0} is hurt far worse than they let on. They will be a liability by the next room.",
        "{0} is hiding a festering wound. Do not stand near them when it opens.",
    ];

    private static readonly string[] Betrayals =
    [
        "{0} was muttering your name in the dark last night. It did not sound kind.",
        "{0} asked the others to vote you out first. They have not said no.",
        "{0} counted the exits twice. Ask yourself who they plan to leave behind.",
    ];

    private static readonly string[] Framings =
    [
        "The dungeon whispers that {0} is the Traitor. Do not turn your back on them.",
        "I have seen {0}'s true face. They are not here to save anyone.",
    ];

    public static IReadOnlyList<Gaslight> Generate(Party party, IRoller roller)
    {
        var alive = party.Members.Where(m => !m.IsDefeated).ToList();
        if (alive.Count < 2)
        {
            return [];
        }

        var result = new List<Gaslight>();
        foreach (var target in alive)
        {
            var others = alive.Where(m => m != target).ToList();
            var subject = others[Pick(roller, others.Count)];
            result.Add(new Gaslight(target.Name, Build(party, subject, roller)));
        }

        return result;
    }

    private static string Build(Party party, PartyMember subject, IRoller roller)
    {
        // A truthful claim (when one exists) keeps the lies believable.
        if (Pick(roller, 100) >= LieChancePercent && party.PocketedGold(subject.Name) is var real and > 0)
        {
            return Format(Thefts[Pick(roller, Thefts.Length)], subject.Name, real);
        }

        var kinds = party.Modifiers.TraitorPact ? 4 : 3;
        return Pick(roller, kinds) switch
        {
            0 => Format(Thefts[Pick(roller, Thefts.Length)], subject.Name, 5 + Pick(roller, 25)),
            1 => Format(Wounds[Pick(roller, Wounds.Length)], subject.Name, 0),
            2 => Format(Betrayals[Pick(roller, Betrayals.Length)], subject.Name, 0),
            _ => Format(Framings[Pick(roller, Framings.Length)], FramedName(party, subject), 0),
        };
    }

    // Never names the real traitor: the point is to point suspicion at someone innocent.
    private static string FramedName(Party party, PartyMember fallback) =>
        string.Equals(party.TraitorName, fallback.Name, StringComparison.OrdinalIgnoreCase)
            ? party.Members.FirstOrDefault(m => !m.IsDefeated && !string.Equals(m.Name, fallback.Name, StringComparison.OrdinalIgnoreCase))?.Name ?? fallback.Name
            : fallback.Name;

    private static string Format(string template, string name, int amount) =>
        string.Format(CultureInfo.InvariantCulture, template, name, amount);

    private static int Pick(IRoller roller, int count) => Math.Abs(roller.Next(count) % count);
}
