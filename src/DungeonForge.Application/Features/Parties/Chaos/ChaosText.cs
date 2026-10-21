using DungeonForge.Domain;

namespace DungeonForge.Application.Features.Parties.Chaos;

// Every sentence a chaos-mode player reads from the server: public events, the claimant's private word and the traitor's
// briefing and reveal. Handlers and the dispatcher decide who hears what; the wording lives only here.
internal static class ChaosText
{
    public const string VaultEmptied = "The vault has been emptied. Nobody says a word.";

    public const string TraitorBriefing =
        "You are the Traitor. Win by seeing the party wiped before floor 5, or by extracting alone while carrying the Cursed Idol. " +
        "Smile. Share. Betray at the right moment.";

    public static string BallotCast(int cast, int required) => $"A ballot was cast ({cast} of {required}).";

    public static string Verdict(ConfrontationResolution resolution)
    {
        var tally = string.Join(", ", resolution.Tally.Where(t => t.Value > 0).Select(t => $"{t.Key} x{t.Value}"));
        var effect = resolution.Outcome switch
        {
            ConfrontationVote.Forgive => "forgiven and keeps the loot",
            ConfrontationVote.DemandCut => $"made to hand {resolution.GoldToParty} gold to the party",
            ConfrontationVote.Banish => "banished, the loot forfeit",
            _ => $"attacked for {resolution.Damage} damage, the loot forfeit",
        };
        return $"Verdict on {resolution.Accused}: {effect}. Ballots: {tally}.";
    }

    public static string Loot(int gold, string? artifact) => artifact is null ? $"{gold} gold" : $"{gold} gold and a {artifact}";

    public static string VaultShared(string who, string loot) => $"{who} declared the vault and shared {loot} with the party.";

    public static string PocketedUnseen(string loot) => $"You pocketed {loot} and nobody saw.";

    public static string CaughtPocketing(string who, string loot, IEnumerable<string> spotters) =>
        $"{who} was caught pocketing {loot}, spotted by {string.Join(", ", spotters)}. " +
        "Vote to forgive, demand a cut, banish or attack.";

    public static string TraitorReveal(string traitor, TraitorOutcome outcome)
    {
        var how = outcome == TraitorOutcome.TraitorWinsWipe
            ? "the party fell before floor 5"
            : "they extracted alone with the Cursed Idol";
        return $"{traitor} was the Traitor, and wins: {how}.";
    }
}
