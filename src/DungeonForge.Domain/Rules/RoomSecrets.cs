using System.Globalization;

namespace DungeonForge.Domain.Rules;

public enum SecretKind
{
    CurseWarning,
    HiddenCoinPocket,
    TrapTruth,
}

public sealed record RoomSecret(string Target, SecretKind Kind, string Text);

// Information asymmetry: on room entry each specialist learns something the rest of the party does not.
// Mage: hidden curse warnings. Bard (the party's rogue-like): hidden coin pockets. Druid (the nearest seer): trap truths.
public static class RoomSecrets
{
    private const int MinPocketGold = 3;
    private const int PocketGoldSpread = 8;

    private static readonly string[] Curses =
    [
        "Your sight reads a curse threaded through this room's stone. Whoever lifts the first prize carries it out. Say nothing yet.",
        "Something here is hexed to follow the greediest hand. You alone can feel it humming.",
        "The air tastes of a binding curse. It will pick a bearer by whoever the room trusts least.",
    ];

    private static readonly string[] Pockets =
    [
        "Your fingers find a hidden pocket in the wall holding {0} gold. No one else noticed.",
        "A loose tile hides a purse of {0} gold. It is yours if you are quiet.",
        "You spot {0} gold wedged behind a seam. The others are looking elsewhere.",
    ];

    private static readonly string[] Traps =
    [
        "The nature of this room rings false to you: one obvious route is trapped, and you know which.",
        "You read the pressure plates correctly. The safest-looking path is the lethal one.",
        "The trap here is real, and it is not where the party is looking.",
    ];

    public static IReadOnlyList<RoomSecret> Generate(IEnumerable<PartyMember> members, RoomType room, int turn, IRoller roller)
    {
        var secrets = new List<RoomSecret>();
        foreach (var member in members.Where(m => !m.IsDefeated))
        {
            switch (member.Class)
            {
                case CharacterClass.Mage:
                    secrets.Add(new RoomSecret(member.Name, SecretKind.CurseWarning, Pick(Curses, roller)));
                    break;
                case CharacterClass.Bard:
                    var gold = MinPocketGold + Math.Abs(roller.Next(PocketGoldSpread) % PocketGoldSpread);
                    var text = string.Format(CultureInfo.InvariantCulture, Pick(Pockets, roller), gold);
                    secrets.Add(new RoomSecret(member.Name, SecretKind.HiddenCoinPocket, text));
                    break;
                case CharacterClass.Druid:
                    secrets.Add(new RoomSecret(member.Name, SecretKind.TrapTruth, Pick(Traps, roller)));
                    break;
            }
        }

        return secrets;
    }

    private static string Pick(string[] set, IRoller roller) => set[Math.Abs(roller.Next(set.Length) % set.Length)];
}
