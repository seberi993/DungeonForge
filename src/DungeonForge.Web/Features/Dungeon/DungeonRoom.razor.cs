using DungeonForge.Shared;
using Microsoft.JSInterop;
using DungeonForge.Web.Shared;

namespace DungeonForge.Web.Features.Dungeon;

// The game screen: state fields, lifecycle and the helpers the markup calls.
public partial class DungeonRoom
{
    private bool _busy;
    private string? _error;
    private bool _scroll;

    private bool _offline;
    private bool _disposed;
    private bool _copied;
    private string _actionText = "";
    private CancellationTokenSource? _narrationCts;

    // Faint rotating hint shown while the action box is empty.
    private readonly Random _rng = new();
    private string _suggestion = "";
    private Timer? _suggestionTimer;

    protected override async Task OnInitializedAsync()
    {
        if (!Session.HasParty)
        {
            Nav.NavigateTo("/");
            return;
        }

        Reroll();
        StartSuggestionTimer();
        _local = Services.GetService(typeof(ILocalNarrator)) as ILocalNarrator;
        if (_local is not null)
        {
            _local.Changed += OnLocalChanged;
            _ = _local.InitializeAsync();
        }

        Hub.TurnResolved += OnTurnResolved;
        Hub.PartyUpdated += OnPartyUpdated;
        Hub.NarrationReady += OnNarrationReady;
        Hub.DiscussionUpdated += OnDiscussionUpdated;
        Hub.TurnLockUpdated += OnTurnLockUpdated;
        Hub.PrivateMessageReceived += OnPrivateMessage;
        Hub.ChaosEventReceived += OnChaosEvent;
        try
        {
            _actionTurn = Session.Turn;
            await Hub.ConnectAsync(Session.PartyId, Session.PlayerName);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _offline = true;
        }
    }

    public async ValueTask DisposeAsync()
    {
        _disposed = true;
        _suggestionTimer?.Dispose();
        if (_local is not null) _local.Changed -= OnLocalChanged;
        Hub.TurnResolved -= OnTurnResolved;
        Hub.PartyUpdated -= OnPartyUpdated;
        Hub.NarrationReady -= OnNarrationReady;
        Hub.DiscussionUpdated -= OnDiscussionUpdated;
        Hub.TurnLockUpdated -= OnTurnLockUpdated;
        Hub.PrivateMessageReceived -= OnPrivateMessage;
        Hub.ChaosEventReceived -= OnChaosEvent;
        _narrationCts?.Cancel();
        _narrationCts?.Dispose();
        await Hub.DisposeAsync();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            try
            {
                var muted = await JS.InvokeAsync<bool>("dungeonForge.sfx.isMuted");
                if (muted != _muted)
                {
                    _muted = muted;
                    StateHasChanged();
                }
            }
            catch (Exception ex) when (ex is JSException or InvalidOperationException or TaskCanceledException) { }
        }

        if (!_scroll) return;
        _scroll = false;
        await JS.InvokeVoidAsync("dungeonForge.scrollToBottom", "log");
    }

    private static string WhisperLabel(string kind) => kind switch
    {
        "whisper" or "ask" => "Whisper",
        "narrator" => "Narrator",
        "secret" => "Secret",
        "traitor" => "Traitor",
        "vault" => "Vault",
        _ => "Dungeon",
    };

    // Outcome text last fully typed; the next scenario waits for it so two blocks never type at once.
    private string? _outcomeTyped;

    // HTML-encoded, with **word** highlights as spans (audit entries are shown whole, not typed).
    private static string Markup(string text) => string.Concat(TypewriterText.Parse(text).Select(s =>
        s.Key ? $"<span class=\"key\">{System.Net.WebUtility.HtmlEncode(s.Text)}</span>" : System.Net.WebUtility.HtmlEncode(s.Text)));

    private static string RoomLabel(RoomType type) => type switch { RoomType.Chest => "chest", RoomType.Puzzle => "puzzle", RoomType.Boss => "boss", _ => "combat" };

    private static string Describe(LogEntry entry) => entry.RoomType is RoomType.Combat or RoomType.Boss
        ? (entry.TargetName is null ? "No damage" : $"{entry.TargetName} took {entry.Damage} damage")
        : entry.RoomType == RoomType.Puzzle && entry.GoldFound == 0 && entry.Damage > 0
            ? $"{entry.TargetName ?? "A hero"} took {entry.Damage} damage"
            : $"Gold +{entry.GoldFound}";
}
