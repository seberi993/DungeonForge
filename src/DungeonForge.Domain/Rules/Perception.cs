namespace DungeonForge.Domain.Rules;

// Passive perception when someone pockets loot: Rogue-like (Bard) and Ranger-like (Druid) classes notice far more.
public static class Perception
{
    public const int BasePercent = 15;
    public const int SpecialistBonus = 25;

    public static int ChancePercent(CharacterClass c) =>
        c is CharacterClass.Bard or CharacterClass.Druid ? BasePercent + SpecialistBonus : BasePercent;

    // One roll per observer, in roster order; the roll must land under the observer's chance.
    public static IReadOnlyList<string> Spotters(IEnumerable<PartyMember> observers, IRoller roller) =>
        [.. observers.Where(o => roller.Next(100) < ChancePercent(o.Class)).Select(o => o.Name)];
}
