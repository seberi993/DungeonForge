namespace DungeonForge.Api.Domain.Rules;

// What a player's action text is trying to do, as far as the odds care.
public enum ActionKind
{
    Other,
    Attack,
    Evade,
    Loot,
}
