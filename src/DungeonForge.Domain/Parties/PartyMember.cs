using DungeonForge.Domain.Rules;

namespace DungeonForge.Domain;

public sealed class PartyMember
{
    public PartyMember(string name, CharacterClass characterClass)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
        Class = characterClass;
        MaxHp = MemberRules.MaxHpFor(characterClass);
        Hp = MaxHp;
    }

    public string Name { get; }

    public CharacterClass Class { get; }

    public int MaxHp { get; }

    public int Hp { get; private set; }

    public bool IsDefeated => Hp == 0;


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
}
