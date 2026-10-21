
namespace DungeonForge.Web.Features.Dungeon;

// The opening-scene phase: the DM asks every player a question and the room stays shut until the whole party has answered.
public sealed partial class GameSession
{
    public bool SetupPending { get; private set; }

    public int SetupAnswered { get; private set; }

    // Setup progress rides on every party broadcast, including same-turn ones the roster logic would otherwise ignore.
    private bool ApplySetup(PartyStateDto party) => ApplySetupProgress(party.SetupPending, party.SetupAnswered);

    // True when the progress moved (so the caller re-renders).
    public bool ApplySetupProgress(bool pending, int answered)
    {
        if (SetupPending == pending && SetupAnswered == answered) return false;
        SetupPending = pending;
        SetupAnswered = answered;
        Changed?.Invoke();
        return true;
    }

    private void ClearSetup()
    {
        SetupPending = false;
        SetupAnswered = 0;
    }
}
