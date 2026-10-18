using DungeonForge.Shared;
using DungeonForge.Web.Models;
using DungeonForge.Web.Services;
using DungeonForge.Web.Components;
using Microsoft.JSInterop;

namespace DungeonForge.Web.Pages;

// What this player does: the action box, lock-in, unlock, explore and the invite code.
public partial class DungeonRoom
{
    private void Reroll()
    {
        _suggestion = ActionSuggestions.Pick(_rng);
    }


    private void StartSuggestionTimer() =>
        _suggestionTimer = new Timer(_ => Dispatch(() =>
        {
            if (!string.IsNullOrEmpty(_actionText)) return false;
            Reroll();
            return true;
        }), null, TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(8));

    private async Task CopyInviteAsync()
    {
        try
        {
            await JS.InvokeVoidAsync("dungeonForge.copyText", Session.InviteCode);
            _copied = true;
        }
        catch (JSException)
        {
            await SelectInvite();
        }
    }

    private async Task SelectInvite()
    {
        try { await JS.InvokeVoidAsync("dungeonForge.selectText", "invite-code"); }
        catch (JSException) { }
    }

    private const int MaxActionLength = 200;
    private int? _lockedTurn;

    // The server's lock status is the source of truth, so a reload or reconnect still shows this player as locked.
    private bool IsLocked => _lockedTurn == Session.Turn
        || Session.LockStatus.Any(s => s.LockedIn && s.Name == Session.PlayerName);

    // A new turn means a new scenario: drop the old answer and lock so the box is ready without any manual clearing.
    private int _actionTurn;

    private void ResetActionForNewTurn()
    {
        if (Session.Turn == _actionTurn) return;
        _actionTurn = Session.Turn;
        _actionText = "";
        _lockedTurn = null;
        Session.SelectedAbilityId = null;
        Reroll();
    }

    private Task ExploreAsync() => SendAsync(null);

    private Task LockInAsync()
    {
        var text = _actionText.Trim();
        return text.Length == 0
            ? Task.CompletedTask
            : SendAsync([new PlayerAction(Session.PlayerId, Session.PlayerName, text)], submitOnly: true);
    }

    private async Task UnlockAsync()
    {
        _busy = true;
        _error = null;
        try
        {
            var result = await Api.UnlockAsync(Session.PartyId, Session.PlayerId);
            switch (result.Status)
            {
                case ApiStatus.Success:
                    _lockedTurn = null;
                    Session.ApplyLockStatus(Session.PartyId, result.Value!.LockStatus);
                    break;
                case ApiStatus.NotFound:
                    Session.Clear();
                    Nav.NavigateTo("/");
                    break;
                default:
                    _error = result.Error ?? "Something went wrong. Try again.";
                    break;
            }
        }
        finally
        {
            _busy = false;
        }
    }

    private async Task SendAsync(IReadOnlyList<PlayerAction>? actions, bool submitOnly = false)
    {
        _busy = true;
        _error = null;
        var locking = actions is { Count: > 0 } && submitOnly;
        try
        {
            // The chosen gift travels beside the lock-in, never inside the public action.
            IReadOnlyList<AbilityIntent>? abilities = locking && Session.SelectedAbilityId is { } gift
                ? [new AbilityIntent(Session.PlayerId, gift)]
                : null;
            var result = await Api.ExploreAsync(Session.PartyId, actions, submitOnly, abilities: abilities);
            switch (result.Status)
            {
                case ApiStatus.Success:
                    var response = result.Value!;
                    var lockedAtTurn = Session.Turn;
                    // Only the request that claimed the narration slot is briefed; the server falls back if we never answer.
                    if (response.Brief is { } brief) _ = NarrateLocallyAsync(brief);
                    // The last lock-in resolves the turn server-side and answers with the resolved (newer) turn.
                    if (Session.Apply(response))
                    {
                        _scroll = true;
                        ResetActionForNewTurn();
                        WatchNarration();
                    }
                    else
                    {
                        if (locking)
                        {
                            _lockedTurn = lockedAtTurn;
                        }
                        if (Session.ApplyPrompt(response)) WatchNarration();
                    }
                    break;
                case ApiStatus.Waiting:
                    if (locking)
                    {
                        _lockedTurn = Session.Turn;
                    }
                    break;
                case ApiStatus.Conflict:
                    Session.MarkDefeated();
                    break;
                case ApiStatus.NotFound:
                    Session.Clear();
                    Nav.NavigateTo("/");
                    break;
                default:
                    _error = result.Error ?? "Something went wrong. Try again.";
                    break;
            }
        }
        finally
        {
            _busy = false;
        }
    }

    private void NewParty()
    {
        Session.Clear();
        Nav.NavigateTo("/");
    }
}
