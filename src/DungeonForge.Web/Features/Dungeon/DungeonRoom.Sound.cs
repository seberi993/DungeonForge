using DungeonForge.Shared;
using Microsoft.JSInterop;

namespace DungeonForge.Web.Features.Dungeon;

// Sound is a nicety: a blocked or missing audio module must never break the game.
public partial class DungeonRoom
{
    private bool _muted;

    private async Task PlayCueAsync(string cue)
    {
        try { await JS.InvokeVoidAsync("dungeonForge.sfx.play", cue); }
        catch (Exception ex) when (ex is JSException or InvalidOperationException or TaskCanceledException) { }
    }

    private async Task ToggleSoundAsync()
    {
        try { _muted = await JS.InvokeAsync<bool>("dungeonForge.sfx.toggle"); }
        catch (Exception ex) when (ex is JSException or InvalidOperationException or TaskCanceledException) { }
    }
}
