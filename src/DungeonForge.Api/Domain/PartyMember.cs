namespace DungeonForge.Api.Domain;

public sealed class PartyMember
{
    public PartyMember(string name, CharacterClass characterClass)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
        Class = characterClass;
        MaxHp = MaxHpFor(characterClass);
        Hp = MaxHp;
    }

    public string Name { get; }

    public CharacterClass Class { get; }

    public int MaxHp { get; }

    public int Hp { get; private set; }

    public bool IsDefeated => Hp == 0;

    // Higher threat is targeted first; ties are resolved by roster order in Party.
    internal int Threat => Class is CharacterClass.Warrior or CharacterClass.Paladin ? 1 : 0;

    internal int TakeDamage(int amount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(amount);
        var applied = Math.Min(amount, Hp);
        Hp -= applied;
        return applied;
    }

    internal int Heal(int amount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(amount);
        if (IsDefeated)
        {
            return 0;
        }

        var applied = Math.Min(amount, MaxHp - Hp);
        Hp += applied;
        return applied;
    }

    private static int MaxHpFor(CharacterClass c) => c switch
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
}
