using DungeonForge.Domain;

namespace DungeonForge.Application.Features.Parties;

// Players no longer pick a class: the UI never shows one. The server still deals each hero a hidden class, by roster
// position, so the stage art, narrator roster and abilities keep working.
public static class HiddenClasses
{
    private static readonly CharacterClass[] Order = Enum.GetValues<CharacterClass>();

    public static CharacterClass For(int rosterIndex) => Order[rosterIndex % Order.Length];
}
