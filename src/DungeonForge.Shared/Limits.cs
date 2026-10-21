namespace DungeonForge.Shared;

// Input limits both sides enforce: the Api rejects what is over them and the browser stops typing at them. This one file is
// linked into DungeonForge.Api and DungeonForge.Web (see their csproj), so a limit has a single definition and the two
// can no longer drift apart.
public static class Limits
{
    // A party holds at most this many heroes.
    public const int MaxMembers = 8;

    public const int MaxPartyNameLength = 40;

    // A hero's name when creating or joining a party.
    public const int MaxMemberNameLength = 32;

    // A player's name as typed into a claim, vote or extract request.
    public const int MaxPlayerNameLength = 40;

    public const int MaxActionTextLength = 200;

    public const int MaxActionNameLength = 40;

    // Actions in one explore request: at most one per member.
    public const int MaxActions = 8;

    // A free-text answer to one of the DM's opening-scene questions.
    public const int MaxSetupAnswerLength = 300;
}
