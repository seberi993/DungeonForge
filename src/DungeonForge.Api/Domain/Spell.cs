namespace DungeonForge.Api.Domain;

public sealed record Spell(string Name, CharacterClass ClassId, ElementType Element, int ManaCost, string Description);
