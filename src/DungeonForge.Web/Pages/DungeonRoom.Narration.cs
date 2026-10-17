using DungeonForge.Shared;
using DungeonForge.Web.Models;
using DungeonForge.Web.Services;
using DungeonForge.Web.Components;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace DungeonForge.Web.Pages;

// Waiting on narration: the stage hand-off, the expiry timer and this browser's local model.
public partial class DungeonRoom
{
    // The stage tells us when it has acted out a turn; until then the outcome and next scenario stay hidden.
    private int _showDoneTurn;
    private bool ShowActive => Session.Show is { } show && show.Turn > _showDoneTurn;

    private void OnShowComplete(int turn) => _showDoneTurn = Math.Max(_showDoneTurn, turn);

    // Longer than the server's client-narration deadline plus its own narrator call, so a slow local model is not abandoned early.
    [Parameter] public TimeSpan NarrationTimeout { get; set; } = TimeSpan.FromSeconds(60);

    private ILocalNarrator? _local;
    private bool _localWriting;

    private void OnLocalChanged() => Dispatch(() => true);

    // Runs the server's prompts on this browser's model and hands the text back; no model (or a decline) lets the server write it.
    private async Task NarrateLocallyAsync(NarrationBrief brief)
    {
        string? text = null;
        if (_local is { State: LocalNarratorState.Ready })
        {
            _localWriting = true;
            await InvokeAsync(StateHasChanged);
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(45));
                text = await _local.GenerateAsync(brief, cts.Token);
            }
            finally
            {
                _localWriting = false;
                if (!_disposed) await InvokeAsync(StateHasChanged);
            }
        }

        await Api.SubmitNarrationAsync(Session.PartyId, brief.Turn, text, declined: text is null);
    }

    private void WatchNarration()
    {
        _narrationCts?.Cancel();
        _narrationCts?.Dispose();
        _narrationCts = null;
        if (!Session.NarrationPending && !Session.ScenarioPending) return;

        var cts = _narrationCts = new CancellationTokenSource();
        var turn = Session.Turn;
        _ = ExpireAfterAsync(turn, cts.Token);
    }

    private async Task ExpireAfterAsync(int turn, CancellationToken ct)
    {
        try
        {
            await Task.Delay(NarrationTimeout, ct);
            await InvokeAsync(() =>
            {
                if (_disposed || !Session.ExpireNarration(turn)) return;
                StateHasChanged();
            });
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
        {
        }
    }
}
