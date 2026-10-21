namespace DungeonForge.Domain.Rules;

// Traitor victory is tracked apart from the ordinary run: a wipe before floor 5, or a solo extraction carrying the Cursed Idol.
public static class TraitorPact
{
    public const string CursedIdol = "Cursed Idol";
    public const int WipeDeadlineFloor = 5;

    public static TraitorOutcome Evaluate(TraitorState s)
    {
        if (s.Extracted && s.TraitorAlive && s.TraitorHoldsIdol && s.OthersAlive == 0)
        {
            return TraitorOutcome.TraitorWinsSoloExtraction;
        }

        // A dead or banished traitor never wins the wipe: PartyWiped means every loyalist is down while the traitor still stands.
        return s.PartyWiped && s.TraitorAlive && s.Floor < WipeDeadlineFloor
            ? TraitorOutcome.TraitorWinsWipe
            : TraitorOutcome.None;
    }
}
