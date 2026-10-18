namespace DungeonForge.Api.Domain;

public sealed record SetupAnswer(string Name, string Text);

public enum SetupSubmit { Accepted, UnknownPlayer, AlreadyAnswered, NotInSetup }

// The opening-scene phase: every member answers one of the DM's questions in free text before the first room exists.
// Internal part of Party; only reached while Party.SyncRoot is held.
internal sealed class SetupState
{
    private readonly Dictionary<string, SetupAnswer> _answers = new(StringComparer.OrdinalIgnoreCase);

    public bool Required { get; set; }

    public bool Forced { get; set; }

    public IReadOnlyList<SetupAnswer> Answers => [.. _answers.Values];

    public bool Has(string name) => _answers.ContainsKey(name);

    public void Add(SetupAnswer answer) => _answers[answer.Name] = answer;

    public int Count => _answers.Count;
}
