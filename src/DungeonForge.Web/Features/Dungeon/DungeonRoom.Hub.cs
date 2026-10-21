using DungeonForge.Shared;
using Microsoft.JSInterop;

namespace DungeonForge.Web.Features.Dungeon;

// Server pushes over SignalR, marshalled onto the render thread.
public partial class DungeonRoom
{
    private void OnTurnResolved(ExploreResponse result)
    {
        if (_disposed) return;
        try
        {
            _ = InvokeAsync(() =>
            {
                if (_disposed || !Session.ApplyRemote(result)) return;
                ResetActionForNewTurn();
                _scroll = true;
                WatchNarration();
                StateHasChanged();
            });
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private void OnNarrationReady(NarrationReady narration) =>
        Dispatch(() => Session.ApplyNarration(narration.PartyId, narration.Turn, narration.Outcome, narration.NextScenario, narration.NextRoomType, narration.NextScene, narration.Source));

    private void OnDiscussionUpdated(Guid partyId, IReadOnlyList<PlayerAction> actions) =>
        Dispatch(() => Session.ApplyDiscussion(partyId, actions));

    // Session raises Changed itself, so the callback only needs to re-render.
    private void Dispatch(Func<bool> apply)
    {
        if (_disposed) return;
        try
        {
            _ = InvokeAsync(() =>
            {
                if (_disposed || !apply()) return;
                ResetActionForNewTurn();
                StateHasChanged();
            });
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private void OnPartyUpdated(PartyStateDto state)
    {
        if (_disposed) return;
        try
        {
            _ = InvokeAsync(() =>
            {
                if (_disposed || !Session.ApplyPartyState(state)) return;
                ResetActionForNewTurn();
                StateHasChanged();
            });
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private void OnPrivateMessage(PrivateMessageDto message) =>
        Dispatch(() => { Session.AddPrivateMessage(message); return true; });

    // Ready markers are redundant with the lock count; vault and reveal announcements are worth showing.
    private void OnChaosEvent(ChaosEventDto chaosEvent)
    {
        if (chaosEvent.Kind == "playerReady") return;
        Dispatch(() => { Session.AddPrivateMessage(new PrivateMessageDto("public", chaosEvent.Text)); return true; });
    }

    private void OnTurnLockUpdated(Guid partyId, IReadOnlyList<LockStatusDto> status) =>
        Dispatch(() => Session.ApplyLockStatus(partyId, status));
}
