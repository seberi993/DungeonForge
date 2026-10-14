using System.Text.Json;
using System.Text.Json.Serialization;
using DungeonForge.Web.Models;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;

namespace DungeonForge.Web.Services;

public sealed class DungeonHubClient(string hubUrl) : IDungeonHubClient
{
    private HubConnection? _connection;

    public event Action<ExploreResponse>? TurnResolved;
    public event Action<PartyStateDto>? PartyUpdated;
    public event Action<NarrationReady>? NarrationReady;
    public event Action<Guid, IReadOnlyList<PlayerAction>>? DiscussionUpdated;
    public event Action<Guid, IReadOnlyList<LockStatusDto>>? TurnLockUpdated;

    public async Task ConnectAsync(Guid partyId, CancellationToken ct = default)
    {
        if (_connection is not null) await DisposeAsync();

        var connection = new HubConnectionBuilder()
            .WithUrl(hubUrl)
            .WithAutomaticReconnect()
            .AddJsonProtocol(o =>
            {
                o.PayloadSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
                o.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
            })
            .Build();

        connection.On<ExploreResponse>("TurnResolved", r => TurnResolved?.Invoke(r));
        connection.On<PartyStateDto>("PartyUpdated", p => PartyUpdated?.Invoke(p));
        connection.On<NarrationReady>("NarrationReady", n => NarrationReady?.Invoke(n));
        connection.On<Guid, List<PlayerAction>>("DiscussionUpdated", (id, a) => DiscussionUpdated?.Invoke(id, a));
        connection.On<Guid, List<LockStatusDto>>("TurnLockUpdated", (id, s) => TurnLockUpdated?.Invoke(id, s));
        connection.Reconnected += _ => RejoinAsync(connection, partyId);
        _connection = connection;

        await connection.StartAsync(ct);
        await connection.InvokeAsync("JoinParty", partyId, ct);
    }

    private static async Task RejoinAsync(HubConnection connection, Guid partyId)
    {
        try
        {
            await connection.InvokeAsync("JoinParty", partyId);
        }
        catch (Exception ex) when (ex is HubException or InvalidOperationException or OperationCanceledException)
        {
            // Party gone or connection dropped again; automatic reconnect handles the latter.
        }
    }

    public async ValueTask DisposeAsync()
    {
        var connection = Interlocked.Exchange(ref _connection, null);
        if (connection is not null) await connection.DisposeAsync();
    }
}
