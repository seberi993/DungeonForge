using DungeonForge.Domain;

namespace DungeonForge.Application.Narration;

// Architectural and grotesque set dressing injected into prompts so rooms stop sounding alike.
public static class GrotesqueVocabulary
{
    // Clichés the narrator must never use; the pool below is checked against this list.
    public static readonly IReadOnlyList<string> ForbiddenTropes = ["torch", "flickering candle", "dusty chest", "cobwebs"];

    public static readonly IReadOnlyList<string> Elements =
    [
        "bone-carved obelisks",
        "bleeding mosaic tiles",
        "echoing machinery behind the walls",
        "pools of mercury",
        "a ceiling of stitched leather",
        "a choir of rusted bellows",
        "weeping basalt columns",
        "a spiral stair made of fused ribs",
        "tiled floors that pulse like a throat",
        "gargoyles with surgical grins",
        "a wall of sealed glass jars, each holding an eye",
        "chains that ring without being touched",
        "a furnace-mouthed idol",
        "ivory pipes breathing warm air",
        "an altar slick with something that is not water",
        "hanging cages lined with polished teeth",
        "mirrors that show the room one second late",
        "a fountain of black oil",
        "murals that rearrange when unwatched",
        "a lattice of copper wire strung with fingerbones",
        "stained-glass windows depicting the party's deaths",
        "a drain in the floor that hums a lullaby",
        "pistons pounding behind a brass door",
        "walls of pressed, grinning masks",
    ];
}

// Rotates through the pool: the roller supplies the variance, a recent-window supplies the "no repeats" guarantee.
// Thread-safe; one instance is shared by every party so consecutive prompts anywhere keep varying.
public sealed class VocabularySelector(IRoller roller, int recentWindow = 8)
{
    private readonly Lock _gate = new();
    private readonly Queue<string> _recent = new();

    public IReadOnlyList<string> Pick(int count = 2)
    {
        lock (_gate)
        {
            var candidates = GrotesqueVocabulary.Elements.Where(e => !_recent.Contains(e)).ToList();
            var chosen = new List<string>(count);
            while (chosen.Count < count && candidates.Count > 0)
            {
                var index = Math.Abs(roller.Next(candidates.Count) % candidates.Count);
                chosen.Add(candidates[index]);
                candidates.RemoveAt(index);
            }

            foreach (var element in chosen)
            {
                _recent.Enqueue(element);
            }

            while (_recent.Count > recentWindow)
            {
                _recent.Dequeue();
            }

            return chosen;
        }
    }
}
