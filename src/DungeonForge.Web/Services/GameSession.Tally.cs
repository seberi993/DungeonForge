using DungeonForge.Web.Models;

namespace DungeonForge.Web.Services;

// Run tally for the end-of-run screen; built only from turns this browser saw resolve.
public sealed partial class GameSession
{
    private readonly Dictionary<string, int> _passes = [];
    private int _roomsWon, _roomsLost, _crits, _fumbles, _worstHit;
    private string? _worstHitName;

    public RunSummary Summary
    {
        get
        {
            // First-listed wins a tie: the party order is stable and a plain max-by would pick an arbitrary dictionary entry.
            var mvp = _members.Select(m => m.Name).Where(_passes.ContainsKey).OrderByDescending(n => _passes[n]).FirstOrDefault();
            return new RunSummary(Turn, Gold, _roomsWon, _roomsLost, _crits, _fumbles,
                mvp, mvp is null ? 0 : _passes[mvp], _worstHitName, _worstHit,
                string.IsNullOrWhiteSpace(LastOutcome) || LastOutcome == NarrationPlaceholder ? null : LastOutcome);
        }
    }

    private void Tally(ExploreResponse result)
    {
        if (result.Success) _roomsWon++; else _roomsLost++;
        foreach (var roll in result.Rolls ?? [])
        {
            if (roll.Roll < roll.Chance) _passes[roll.Name] = _passes.GetValueOrDefault(roll.Name) + 1;
            if (roll.Crit) _crits++;
            if (roll.Fumble) _fumbles++;
        }

        foreach (var hit in result.Hits ?? [])
        {
            if (hit.Damage <= _worstHit) continue;
            _worstHit = hit.Damage;
            _worstHitName = hit.Name;
        }
    }
}
