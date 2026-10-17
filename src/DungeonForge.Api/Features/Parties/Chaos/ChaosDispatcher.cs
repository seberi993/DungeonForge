using DungeonForge.Api.Domain.Rules;
using DungeonForge.Api.Domain;
using DungeonForge.Api.Errors;
using DungeonForge.Api.Features.Parties.ExploreDungeon;
using DungeonForge.Api.Features.Parties.Realtime;
using DungeonForge.Api.Infrastructure.Narration;

namespace DungeonForge.Api.Features.Parties.Chaos;

// Single door for everything chaos mode pushes to clients. Private data goes through SendPrivateAsync only; the public side
// carries nothing but ready markers, vault/confrontation status and post-resolution reveals. Delivery failures are logged,
// never thrown: a flaky socket must not fail a game request.
public static class ChaosOperations
{
    public const string Send = "Chaos:Send";
}

public sealed class ChaosDispatcher(IPartyNotifier notifier, IRoller roller, ILogger<ChaosDispatcher> logger)
{
    // ---- Information asymmetry ----

    public async Task DispatchRoomSecretsAsync(Party party, RoomType room, int turn)
    {
        if (!party.Modifiers.Chaos)
        {
            return;
        }

        foreach (var secret in RoomSecrets.Generate(party.Members, room, turn, roller))
        {
            await SendAsync(party.Id, secret.Target, new PrivateMessage(PrivateKinds.Secret, secret.Text));
        }
    }

    // Chaos mode only: a private, mostly false claim about another member for every living player, every turn.
    public async Task DispatchGaslightingAsync(Party party)
    {
        if (!party.Modifiers.Chaos)
        {
            return;
        }

        foreach (var lie in Gaslighting.Generate(party, roller))
        {
            await SendAsync(party.Id, lie.Target, new PrivateMessage(PrivateKinds.Whisper, lie.Text));
        }
    }

    // Whisper targets are a member name or a class name; unknown targets are dropped.
    public async Task DispatchWhispersAsync(Party party, IReadOnlyList<Whisper> whispers)
    {
        foreach (var whisper in whispers)
        {
            if (ResolveTarget(party, whisper.Target) is { } target)
            {
                await SendAsync(party.Id, target.Name, new PrivateMessage(PrivateKinds.Whisper, whisper.Text));
            }
        }
    }

    private static PartyMember? ResolveTarget(Party party, string target)
    {
        var members = party.Members;
        return members.FirstOrDefault(m => string.Equals(m.Name, target, StringComparison.OrdinalIgnoreCase))
            ?? (Enum.TryParse<CharacterClass>(target, ignoreCase: true, out var cls)
                ? members.FirstOrDefault(m => m.Class == cls && !m.IsDefeated) ?? members.FirstOrDefault(m => m.Class == cls)
                : null);
    }

    // ---- Public, information-free announcements ----

    public Task AnnouncePlayerReadyAsync(Guid partyId, string playerName) =>
        PublishAsync(partyId, new ChaosEvent(ChaosEvent.PlayerReady, $"{playerName} is ready"));

    public Task AnnounceVaultAsync(Guid partyId, UnclaimedVault vault)
    {
        var loot = vault.Artifact is null ? $"{vault.Gold} gold" : $"{vault.Gold} gold and a {vault.Artifact}";
        return PublishAsync(partyId, new ChaosEvent(ChaosEvent.Vault, $"An unclaimed vault holds {loot}. Declare and share it, or secretly pocket it."));
    }

    public Task PublishAsync(Guid partyId, ChaosEvent chaosEvent) =>
        GuardAsync(() => notifier.ChaosEventAsync(partyId, chaosEvent, CancellationToken.None), $"chaos event {chaosEvent.Kind}", partyId);

    public Task SendAsync(Guid partyId, string playerName, PrivateMessage message) =>
        GuardAsync(() => notifier.SendPrivateAsync(partyId, playerName, message, CancellationToken.None), $"private {message.Kind}", partyId);

    // ---- Hidden Traitor Pact ----

    // Called whenever a player connects: picks the traitor once enough players exist, and tells ONLY the traitor.
    public async Task EnsureTraitorAsync(Party party, string? registeringPlayer)
    {
        if (!party.Modifiers.TraitorPact)
        {
            return;
        }

        var assigned = party.TryAssignTraitor(roller);
        if (party.TraitorName is not { } traitor)
        {
            return;
        }

        if (assigned || string.Equals(traitor, registeringPlayer, StringComparison.OrdinalIgnoreCase))
        {
            await SendAsync(party.Id, traitor, new PrivateMessage(PrivateKinds.Traitor, ChaosText.TraitorBriefing));
        }
    }

    // Run-ending reveal: only now does the traitor's identity become public.
    public Task AnnounceTraitorOutcomeAsync(Party party, TraitorOutcome outcome)
    {
        if (outcome == TraitorOutcome.None || party.TraitorName is not { } traitor)
        {
            return Task.CompletedTask;
        }

        return PublishAsync(party.Id, new ChaosEvent(ChaosEvent.TraitorOutcome, ChaosText.TraitorReveal(traitor, outcome)));
    }

    private Task GuardAsync(Func<Task> send, string what, Guid partyId) =>
        BestEffort.RunAsync(logger, ChaosOperations.Send, partyId, what, send);
}
