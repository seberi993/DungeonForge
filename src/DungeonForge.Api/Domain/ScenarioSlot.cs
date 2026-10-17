using DungeonForge.Api.Domain.Rules;

namespace DungeonForge.Api.Domain;

// The encounter in front of the party: either a scenario being narrated (AwaitingRoom, claimed for one turn) or a stored one
// (Pending) that members discuss and act on. Claiming the slot is what guarantees a single narrator call per turn. Part of a
// Party: no lock of its own, the caller holds the party's SyncRoot.
internal sealed class ScenarioSlot
{
    private int _awaitingTurn;

    public PendingEncounter? Pending { get; private set; }

    // Room of the scenario being generated (rolled in C#); null when idle or a scenario is already pending.
    public RoomType? AwaitingRoom { get; private set; }

    // Claims the right to generate the next scenario. False when one is pending or already being generated.
    public bool TryBegin(RoomType room, int turn)
    {
        if (Pending is not null || AwaitingRoom is not null)
        {
            return false;
        }

        AwaitingRoom = room;
        _awaitingTurn = turn;
        return true;
    }

    // True while a scenario is being generated for exactly this turn: only the first writer for the claim may complete it.
    public bool Awaits(int turn) => AwaitingRoom is not null && _awaitingTurn == turn;

    public PendingEncounter Complete(PendingEncounter encounter)
    {
        AwaitingRoom = null;
        return Pending = encounter;
    }

    public void Abort() => AwaitingRoom = null;

    public void ClearPending() => Pending = null;

    // One action per member per turn: a resubmit replaces the earlier one in place. False when no encounter is pending or
    // the discussion is full.
    public bool TrySubmit(PlayerAction action)
    {
        if (Pending is null)
        {
            return false;
        }

        var list = Pending.Discussion?.ToList() ?? [];
        var at = list.FindIndex(a => a.MemberId == action.MemberId);
        if (at >= 0)
        {
            list[at] = action;
        }
        else if (list.Count >= PartyRules.MaxDiscussionEntries)
        {
            return false;
        }
        else
        {
            list.Add(action);
        }

        Pending = Pending with { Discussion = list };
        return true;
    }

    public void RemoveFromDiscussion(Guid memberId)
    {
        if (Pending?.Discussion is { } discussion)
        {
            Pending = Pending with { Discussion = discussion.Where(a => a.MemberId != memberId).ToList() };
        }
    }
}
