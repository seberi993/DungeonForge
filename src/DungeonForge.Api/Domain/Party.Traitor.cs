using DungeonForge.Api.Domain.Rules;

namespace DungeonForge.Api.Domain;

// The hidden traitor pact: who the traitor is, solo extraction, and the latched victory. The traitor's identity never reaches
// PartySnapshot. State lives in ChaosState; every member here runs under SyncRoot.
public sealed partial class Party
{
    // The latched traitor victory (None until one is reached); EvaluateTraitor reports each victory only once.
    public TraitorOutcome TraitorResult { get { lock (SyncRoot) { return _chaos.TraitorOutcome; } } }

    public string? TraitorName { get { lock (SyncRoot) { return _chaos.Traitor; } } }

    // Picks the traitor once, from the active members, as soon as there is someone to betray. False when already chosen.
    public bool TryAssignTraitor(IRoller roller)
    {
        lock (SyncRoot)
        {
            if (!Modifiers.TraitorPact || _chaos.Traitor is not null || Members.Count < 2)
            {
                return false;
            }

            var pool = Members.Where(IsInPlay).ToArray();
            if (pool.Length == 0)
            {
                return false;
            }

            _chaos.Traitor = pool[roller.Next(pool.Length) % pool.Length].Name;
            return true;
        }
    }

    public bool TryExtract(string name)
    {
        lock (SyncRoot)
        {
            // One-shot, and only for someone still in play; the extractor leaves the dungeon (see IsInPlay).
            if (_chaos.ExtractedBy is not null || Active(name) is not { } member)
            {
                return false;
            }

            _chaos.ExtractedBy = member.Name;
            SettleVaultIfWiped();
            return true;
        }
    }

    public TraitorOutcome EvaluateTraitor()
    {
        lock (SyncRoot)
        {
            // Latch: a victory is reported exactly once; later calls (votes, turns, extractions) return None.
            if (!Modifiers.TraitorPact || _chaos.Traitor is not { } traitor || _chaos.TraitorOutcome != TraitorOutcome.None)
            {
                return TraitorOutcome.None;
            }

            var traitorStanding = Members.Any(m => string.Equals(m.Name, traitor, StringComparison.OrdinalIgnoreCase)
                && !m.IsDefeated && !_chaos.Banished.Contains(m.Name));
            var othersAlive = Members.Count(m => m.Name != traitor && IsInPlay(m));
            return _chaos.TraitorOutcome = TraitorPact.Evaluate(new TraitorState(
                _progress.Floor,
                othersAlive == 0,
                traitorStanding,
                othersAlive,
                string.Equals(ArtifactHolder(TraitorPact.CursedIdol), traitor, StringComparison.OrdinalIgnoreCase),
                string.Equals(_chaos.ExtractedBy, traitor, StringComparison.OrdinalIgnoreCase)));
        }
    }
}
