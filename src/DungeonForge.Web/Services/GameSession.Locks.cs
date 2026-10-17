using DungeonForge.Web.Models;

namespace DungeonForge.Web.Services;

// The party's shared discussion and who has locked in, as the server broadcasts them.
public sealed partial class GameSession
{
    public bool ApplyLockStatus(Guid partyId, IReadOnlyList<LockStatusDto> status)
    {
        if (partyId != PartyId) return false;
        LockStatus = [.. status];
        Changed?.Invoke();
        return true;
    }

    public bool ApplyDiscussion(Guid partyId, IReadOnlyList<PlayerAction> actions)
    {
        if (partyId != PartyId) return false;
        Discussion = [.. actions];
        Changed?.Invoke();
        return true;
    }

    // Own text (if any) replaces this player's shared action; teammates' non-blank shared actions are kept.
    public IReadOnlyList<PlayerAction> BuildActions(string? ownText)
    {
        var own = ownText?.Trim();
        var actions = Discussion
            .Where(a => !string.IsNullOrWhiteSpace(a.Text) && (a.MemberId != PlayerId || string.IsNullOrEmpty(own)))
            .ToList();
        if (!string.IsNullOrEmpty(own)) actions.Add(new PlayerAction(PlayerId, PlayerName, own));
        return actions;
    }
}
