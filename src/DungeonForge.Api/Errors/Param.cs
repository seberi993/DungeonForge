namespace DungeonForge.Api.Errors;

// A reproduction input attached to an Error. Only the factories below can make one, so parameters stay on a fixed
// allow-list: request echoes and public game state. Never traitor identity, pocket state, secrets, keys or tokens.
public readonly record struct Param(string Key, string Value)
{
    private const int MaxPlayerNameLength = 40;
    private const int MaxCodeLength = 16;

    public static IReadOnlyCollection<string> AllowedKeys { get; } =
        ["partyId", "memberId", "turn", "floor", "playerName", "room", "locked", "required", "code"];

    public static Param PartyId(Guid id) => new("partyId", id.ToString());

    public static Param MemberId(Guid id) => new("memberId", id.ToString());

    public static Param Turn(int turn) => new("turn", turn.ToString());

    public static Param Floor(int floor) => new("floor", floor.ToString());

    public static Param PlayerName(string name) => new("playerName", Preview(name, MaxPlayerNameLength));

    public static Param Room(string room) => new("room", room);

    public static Param Locked(int count) => new("locked", count.ToString());

    public static Param Required(int count) => new("required", count.ToString());

    public static Param Code(string code) => new("code", Preview(code, MaxCodeLength));

    private static string Preview(string value, int max) => value.Length <= max ? value : value[..max];
}
