namespace DungeonForge.Api.Domain;

public enum CovertKind
{
    Backstab,
    Sabotage,
    Steal,
}

// A secret second plan riding on a member's overt action. Hidden from the party until the Master Turn resolves.
public sealed record CovertIntent(CovertKind Kind, string Target);
