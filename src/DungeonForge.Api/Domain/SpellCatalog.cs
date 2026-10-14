namespace DungeonForge.Api.Domain;

// Static spellbooks; flavor only here, game math stays in the explore handler.
public static class SpellCatalog
{
    private static readonly Dictionary<CharacterClass, IReadOnlyList<Spell>> Books = new()
    {
        [CharacterClass.Warrior] =
        [
            new("Cleaving Strike", CharacterClass.Warrior, ElementType.Physical, 3, "A wide sweep that hits every foe in reach."),
            new("Shield Bash", CharacterClass.Warrior, ElementType.Physical, 2, "Slam a foe to stun it and draw its attention."),
            new("Battle Cry", CharacterClass.Warrior, ElementType.Physical, 4, "A roar that steels the party's nerve."),
            new("Flame-Forged Blade", CharacterClass.Warrior, ElementType.Fire, 5, "Wreathe the weapon in fire for a searing blow."),
        ],
        [CharacterClass.Mage] =
        [
            new("Fireball", CharacterClass.Mage, ElementType.Fire, 5, "Hurl an explosive orb of flame."),
            new("Frost Lance", CharacterClass.Mage, ElementType.Ice, 4, "A shard of ice that pierces and slows."),
            new("Arcane Ward", CharacterClass.Mage, ElementType.Physical, 3, "A shimmering barrier against harm."),
            new("Chain Spark", CharacterClass.Mage, ElementType.Fire, 6, "Lightning-hot embers leap between enemies."),
        ],
        [CharacterClass.Druid] =
        [
            new("Entangling Roots", CharacterClass.Druid, ElementType.Physical, 3, "Roots erupt to bind foes in place."),
            new("Rejuvenate", CharacterClass.Druid, ElementType.Holy, 4, "Gentle growth mends an ally's wounds."),
            new("Frostbloom", CharacterClass.Druid, ElementType.Ice, 4, "Icy petals chill everything they touch."),
            new("Wildfire", CharacterClass.Druid, ElementType.Fire, 5, "Spread a hungry blaze across the ground."),
        ],
        [CharacterClass.Paladin] =
        [
            new("Smite", CharacterClass.Paladin, ElementType.Holy, 4, "Radiant force falls upon a single foe."),
            new("Lay on Hands", CharacterClass.Paladin, ElementType.Holy, 5, "Channel light to heal a wounded ally."),
            new("Aegis of Dawn", CharacterClass.Paladin, ElementType.Holy, 3, "A halo of light shields the party."),
            new("Judgment", CharacterClass.Paladin, ElementType.Physical, 4, "A sworn strike that punishes the wicked."),
        ],
        [CharacterClass.Priest] =
        [
            new("Heal", CharacterClass.Priest, ElementType.Holy, 3, "Restore a fallen ally's strength."),
            new("Holy Light", CharacterClass.Priest, ElementType.Holy, 4, "A bright beam that burns the unholy."),
            new("Sanctuary", CharacterClass.Priest, ElementType.Holy, 5, "Hallow the ground so the party may rest."),
            new("Cleansing Prayer", CharacterClass.Priest, ElementType.Holy, 2, "Lift curses and sickness from an ally."),
        ],
        [CharacterClass.Necromancer] =
        [
            new("Raise Thrall", CharacterClass.Necromancer, ElementType.Unholy, 5, "Summon a skeletal servant to fight for you."),
            new("Drain Life", CharacterClass.Necromancer, ElementType.Unholy, 4, "Siphon vitality from a foe into yourself."),
            new("Bone Spear", CharacterClass.Necromancer, ElementType.Unholy, 3, "A spear of bone impales the target."),
            new("Grave Chill", CharacterClass.Necromancer, ElementType.Ice, 4, "A tomb-cold wind saps enemy strength."),
        ],
        [CharacterClass.Bard] =
        [
            new("Inspiring Song", CharacterClass.Bard, ElementType.Holy, 3, "A rousing melody lifts the party's spirits."),
            new("Discordant Chord", CharacterClass.Bard, ElementType.Physical, 3, "A jarring note that rattles enemies."),
            new("Lullaby", CharacterClass.Bard, ElementType.Ice, 4, "A soft tune that lulls foes into stillness."),
            new("Fiery Crescendo", CharacterClass.Bard, ElementType.Fire, 5, "A blazing finale that scorches the room."),
        ],
    };

    public static IReadOnlyList<Spell> For(CharacterClass cls) => Books.GetValueOrDefault(cls) ?? [];
}
