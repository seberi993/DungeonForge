using DungeonForge.Web.Models;

namespace DungeonForge.Web.Services;

// Mana is a client-side estimate for this player's own hero; the server has no mana pool, so these numbers live here.
public static class ManaRules
{
    public const int RegenPerTurn = 10;

    public static int MaxFor(CharacterClass cls) => cls switch
    {
        CharacterClass.Warrior => 20,
        CharacterClass.Paladin => 40,
        CharacterClass.Bard => 50,
        CharacterClass.Druid => 60,
        CharacterClass.Priest => 60,
        CharacterClass.Necromancer => 70,
        CharacterClass.Mage => 80,
        _ => 40
    };
}
