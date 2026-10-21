using DungeonForge.Application.Features.Social.Core;

namespace DungeonForge.Application.Features.Social.Peers;

public enum PeerChoice
{
    Yes,
    Maybe,
    No,
}

// One question about one teammate; resolves once enough teammates have answered.
public sealed class PeerRound(string id, string about, string questionId)
{
    public string Id { get; } = id;

    public string About { get; } = about;

    public string QuestionId { get; } = questionId;

    public Dictionary<string, PeerChoice> Responses { get; } = new(StringComparer.OrdinalIgnoreCase);

    public bool Resolved { get; set; }
}

public sealed record PeerQuestion(
    string Id, string Text, Axis Primary, int Sign, Axis Secondary, Axis Cross, int CrossSign)
{
    public string TextFor(string name) => Text.Replace("{Name}", name, StringComparison.Ordinal);
}

public static class PeerQuestions
{
    public static readonly IReadOnlyList<PeerQuestion> All =
    [
        new("Q1", "Would {Name} hold the door while the rest flee?", Axis.Valor, 1, Axis.Resolve, Axis.Discipline, 1),
        new("Q2", "Would {Name} pocket a coin when no one is looking?", Axis.Greed, 1, Axis.Cunning, Axis.Empathy, -1),
        new("Q3", "Could {Name} talk us out of trouble?", Axis.Cunning, 1, Axis.Empathy, Axis.Lore, 1),
        new("Q4", "Would {Name} panic first?", Axis.Resolve, -1, Axis.Chaos, Axis.Valor, -1),
        new("Q5", "Would you trust {Name} with the map?", Axis.Discipline, 1, Axis.Lore, Axis.Empathy, 1),
        new("Q6", "Would {Name} pick up the shiny cursed thing?", Axis.Chaos, 1, Axis.Greed, Axis.Discipline, -1),
        new("Q7", "Would {Name} tend the wounded first?", Axis.Empathy, 1, Axis.Resolve, Axis.Discipline, 1),
        new("Q8", "Could {Name} read the old runes?", Axis.Lore, 1, Axis.Discipline, Axis.Cunning, 1),
        new("Q9", "Would {Name} lie to your face, kindly?", Axis.Cunning, 1, Axis.Empathy, Axis.Chaos, 1),
        new("Q10", "Would {Name} keep going when it is hopeless?", Axis.Resolve, 1, Axis.Valor, Axis.Discipline, 1),
    ];

    public static PeerQuestion? Find(string id) => All.FirstOrDefault(q => q.Id == id);
}

public sealed record PeerParams
{
    public int MaxDelta { get; init; } = 6;

    public double UnanimityBonus { get; init; } = 1.5;

    public int Noise { get; init; } = 1;

    // A round resolves once this many teammates answered (or all of them, in a small party).
    public int ResolveAt { get; init; } = 3;

    public double SecondaryWeight { get; init; } = 0.5;

    public double CrossWeight { get; init; } = 0.25;

    // Most a single axis can move because of teammates over a whole run.
    public int TraitCap { get; init; } = 40;

    // Prompts every player may answer before the run, plus one more per floor reached.
    public int StartingPrompts { get; init; } = 3;
}

public sealed record PeerResult(IReadOnlyDictionary<Axis, int> Deltas, bool Unanimous, int Score);

// Pure: answers in, axis deltas out. Neutral rounds stay neutral (no noise on zero).
public static class PeerAggregator
{
    public static PeerResult Aggregate(PeerQuestion question, IReadOnlyCollection<PeerChoice> responses, PeerParams p, SeededRng rng)
    {
        if (responses.Count == 0)
        {
            return new PeerResult(new Dictionary<Axis, int>(), false, 0);
        }

        var score = responses.Average(r => r switch { PeerChoice.Yes => 1.0, PeerChoice.No => -1.0, _ => 0.0 });
        var unanimous = responses.Count >= 3 && (responses.All(r => r == PeerChoice.Yes) || responses.All(r => r == PeerChoice.No));
        var raw = p.MaxDelta * score * question.Sign * (unanimous ? p.UnanimityBonus : 1.0);

        var deltas = new Dictionary<Axis, int>();
        void Put(Axis axis, double value)
        {
            var rounded = (int)Math.Round(value, MidpointRounding.AwayFromZero);
            if (rounded != 0)
            {
                deltas[axis] = deltas.GetValueOrDefault(axis) + rounded;
            }
        }

        Put(question.Primary, raw);
        Put(question.Secondary, raw * p.SecondaryWeight);
        Put(question.Cross, raw * p.CrossWeight * question.CrossSign);

        if (p.Noise > 0 && deltas.TryGetValue(question.Primary, out var primary))
        {
            deltas[question.Primary] = primary + rng.Between(-p.Noise, p.Noise);
        }

        return new PeerResult(deltas, unanimous, Math.Sign(score));
    }
}
