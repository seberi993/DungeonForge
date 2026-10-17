namespace DungeonForge.Api.Domain.Rules;

// Per-class numbers for a party member.
public static class MemberRules
{
    public static int MaxHpFor(CharacterClass c) => c switch
    {
        CharacterClass.Warrior => 30,
        CharacterClass.Paladin => 28,
        CharacterClass.Druid => 22,
        CharacterClass.Priest => 20,
        CharacterClass.Bard => 20,
        CharacterClass.Necromancer => 18,
        CharacterClass.Mage => 16,
        _ => throw new ArgumentOutOfRangeException(nameof(c), c, "Unknown character class."),
    };

    // Higher threat is targeted first; ties are resolved by roster order in Party.
    public static int Threat(CharacterClass c) => c is CharacterClass.Warrior or CharacterClass.Paladin ? 1 : 0;
}
