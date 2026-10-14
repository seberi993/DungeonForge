namespace DungeonForge.Api.Features.Parties.Chaos;

// Public chaos-mode broadcast. Kind is a stable client-facing tag; Text is human readable and never carries hidden state.
public sealed record ChaosEvent(string Kind, string Text)
{
    public const string PlayerReady = "playerReady";
    public const string Vault = "vault";
    public const string VaultClaimed = "vaultClaimed";
    public const string Confrontation = "confrontation";
    public const string ConfrontationResolved = "confrontationResolved";
    public const string TraitorOutcome = "traitorOutcome";
}

// Delivered to a single player's connection only.
public sealed record PrivateMessage(string Kind, string Text);

public static class PrivateKinds
{
    public const string Secret = "secret";
    public const string Whisper = "whisper";
    public const string Traitor = "traitor";
    public const string Vault = "vault";
}
