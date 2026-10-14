using DungeonForge.Api.Domain;

namespace DungeonForge.Api.Features.Classes.GetClassSpells;

public static class ClassIdParser
{
    // Accepts an enum name (case-insensitive) or its integer value; anything else is null.
    public static CharacterClass? Parse(string id) =>
        int.TryParse(id, out var n)
            ? Enum.IsDefined(typeof(CharacterClass), n) ? (CharacterClass)n : null
            : Enum.TryParse<CharacterClass>(id, ignoreCase: true, out var cls) && Enum.IsDefined(cls) ? cls : null;
}
