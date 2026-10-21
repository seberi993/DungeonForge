using DungeonForge.Domain;

namespace DungeonForge.Tests.Domain;

// Test setup that reaches into a party's members. Production hurts members only through Party.ApplyMemberOutcomes; tests
// need to put a hero in a given state first, and should not depend on a damage-targeting rule to do it.
internal static class PartyTestSetup
{
    public static int Hurt(this Party party, string memberName, int amount) => Member(party, memberName).TakeDamage(amount);

    public static void Defeat(this Party party, string memberName) => Member(party, memberName).TakeDamage(int.MaxValue);

    private static PartyMember Member(Party party, string name) => party.Members.Single(m => m.Name == name);
}
