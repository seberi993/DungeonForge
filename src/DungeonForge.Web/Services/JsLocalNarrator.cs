using DungeonForge.Web.Models;
using Microsoft.JSInterop;

namespace DungeonForge.Web.Services;

// Thin wrapper over wwwroot/js/localNarrator.js; the model itself lives in the browser (WebLLM).
public sealed class JsLocalNarrator(IJSRuntime js) : ILocalNarrator, IAsyncDisposable
{
    private IJSObjectReference? _module;
    private DotNetObjectReference<JsLocalNarrator>? _self;
    private bool _initialized;

    public LocalNarratorState State { get; private set; } = LocalNarratorState.Unknown;
    public int Progress { get; private set; }
    public string? Status { get; private set; }
    public event Action? Changed;

    public async Task InitializeAsync()
    {
        if (_initialized) return;
        _initialized = true;
        try
        {
            _module = await js.InvokeAsync<IJSObjectReference>("import", "./js/localNarrator.js");
            if (!await _module.InvokeAsync<bool>("isSupported"))
            {
                Set(LocalNarratorState.Unsupported);
                return;
            }

            Set(LocalNarratorState.Idle);
            if (await _module.InvokeAsync<bool>("getOptIn"))
            {
                await EnableAsync();
            }
        }
        catch (Exception ex) when (ex is JSException or InvalidOperationException or HttpRequestException)
        {
            Set(LocalNarratorState.Unsupported, ex.Message);
        }
    }

    public async Task EnableAsync()
    {
        if (_module is null || State is not (LocalNarratorState.Idle or LocalNarratorState.Failed)) return;

        Progress = 0;
        Set(LocalNarratorState.Downloading, "Starting...");
        try
        {
            await _module.InvokeVoidAsync("setOptIn", true);
            _self ??= DotNetObjectReference.Create(this);
            await _module.InvokeVoidAsync("load", _self);
            Set(LocalNarratorState.Ready, "Local narrator ready");
        }
        catch (JSException ex)
        {
            Set(LocalNarratorState.Failed, ex.Message);
        }
    }

    public async Task<string?> GenerateAsync(NarrationBrief brief, CancellationToken ct = default)
    {
        if (_module is null || State != LocalNarratorState.Ready) return null;
        try
        {
            var text = await _module.InvokeAsync<string?>(
                "generate", ct, brief.System, brief.User, brief.JsonSchema, brief.MaxTokens);
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
        catch (Exception ex) when (ex is JSException or TaskCanceledException or OperationCanceledException)
        {
            return null;
        }
    }

    [JSInvokable]
    public void OnProgress(int percent, string text)
    {
        Progress = percent;
        Status = text;
        Changed?.Invoke();
    }

    private void Set(LocalNarratorState state, string? status = null)
    {
        State = state;
        Status = status;
        Changed?.Invoke();
    }

    public async ValueTask DisposeAsync()
    {
        _self?.Dispose();
        if (_module is not null)
        {
            try { await _module.DisposeAsync(); }
            catch (JSDisconnectedException) { }
        }
    }
}
