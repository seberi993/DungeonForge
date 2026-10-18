using DungeonForge.Api.Domain.Rules;

namespace DungeonForge.Api.Domain;

// The opening-scene phase (see SetupState). Opt-in per party: parties made without it start at once.
public sealed partial class Party
{
    private readonly SetupState _setup = new();

    private const int RecentOutcomeLimit = 3;
    private readonly Queue<string> _recentOutcomes = new();

    // The DM's last few outcome lines, oldest first; fed back to the narrator so it varies its wording.
    public IReadOnlyList<string> RecentOutcomes { get { lock (SyncRoot) { return [.. _recentOutcomes]; } } }

    public void RecordOutcome(string outcome)
    {
        if (string.IsNullOrWhiteSpace(outcome)) return;

        lock (SyncRoot)
        {
            _recentOutcomes.Enqueue(outcome);
            while (_recentOutcomes.Count > RecentOutcomeLimit) _recentOutcomes.Dequeue();
        }
    }

    private StoryLean _lean;

    // What the party's own words lean toward so far; it nudges the next room roll and is named to the narrator.
    public StoryLean StoryLean { get { lock (SyncRoot) { return _lean; } } }

    // Tallies free text (an opening answer or a turn's action) into the lean. Only story-mode parties (those that painted an
    // opening scene) lean; others keep the plain uniform room roll.
    public void NoteStory(string? text)
    {
        lock (SyncRoot)
        {
            if (_setup.Required)
            {
                _lean = _lean.Plus(StoryTags.Score(text));
            }
        }
    }

    public void RequireSetup()
    {
        lock (SyncRoot)
        {
            _setup.Required = true;
        }
    }

    // True while the party still owes the DM its opening answers: no scenario may be generated yet.
    public bool SetupPending
    {
        get
        {
            lock (SyncRoot)
            {
                return _setup.Required && !_setup.Forced && _setup.Count < Members.Count;
            }
        }
    }

    public IReadOnlyList<SetupAnswer> SetupAnswers { get { lock (SyncRoot) { return _setup.Answers; } } }

    public int SetupAnswered { get { lock (SyncRoot) { return _setup.Count; } } }

    public bool HasAnsweredSetup(string name) { lock (SyncRoot) { return _setup.Has(name); } }

    public SetupSubmit TrySubmitSetup(string name, string text, bool forceStart = false)
    {
        lock (SyncRoot)
        {
            if (!_setup.Required || _turn > 0 || _scenario.Pending is not null)
            {
                return SetupSubmit.NotInSetup;
            }

            var member = Members.FirstOrDefault(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase));
            if (member is null)
            {
                return SetupSubmit.UnknownPlayer;
            }

            if (_setup.Has(member.Name))
            {
                // An answered player may still call the party to begin without the stragglers.
                _setup.Forced |= forceStart;
                return forceStart ? SetupSubmit.Accepted : SetupSubmit.AlreadyAnswered;
            }

            _setup.Add(new SetupAnswer(member.Name, text));
            NoteStory(text);
            _setup.Forced |= forceStart;
            return SetupSubmit.Accepted;
        }
    }
}
