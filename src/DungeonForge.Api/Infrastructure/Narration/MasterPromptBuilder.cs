using System.Text;
using DungeonForge.Api.Features.Parties.ExploreDungeon;

namespace DungeonForge.Api.Infrastructure.Narration;

// Composes ONE prompt per turn from the room, party status, the resolved result and every member's input.
public static class MasterPromptBuilder
{
    public const int MaxLength = 1500;
    public const int MaxActionTextLength = 200;
    public const int MaxNameLength = 40;
    public const int MaxOptionLength = 80;

    private const string NeutralPartyName = "the party";
    private const string NeutralOption = "the chosen action";
    private const string NeutralMember = "A member";

    public static string Scene(RoomType room) => room switch
    {
        RoomType.Combat => "a combat encounter",
        RoomType.Puzzle => "a puzzle room",
        _ => "a treasure chest",
    };

    public static string Mood(bool wounded) => wounded ? "battered and weary" : "healthy and confident";

    public static string Build(string chosenOption, bool success, NarrationContext ctx)
    {
        var sb = new StringBuilder()
            .Append("Party: ").Append(NarrationText.Sanitize(ctx.PartyName, MaxNameLength, NeutralPartyName))
            .Append(". Scene: ").Append(Scene(ctx.Room))
            .Append(". Mood: the party is ").Append(Mood(ctx.PartyWounded))
            .Append(". Chosen action: ").Append(NarrationText.Sanitize(chosenOption, MaxOptionLength, NeutralOption))
            .Append(". Result: the action ").Append(success ? "succeeded" : "failed").Append('.');

        var first = true;
        foreach (var action in ctx.Actions)
        {
            var text = NarrationText.Clean(action.Text, MaxActionTextLength);
            if (text.Length == 0)
            {
                continue;
            }

            var entry = $"{NarrationText.Sanitize(action.Name, MaxNameLength, NeutralMember)}: {text}";
            var prefix = first ? " Member inputs: " : " | ";
            if (sb.Length + prefix.Length + entry.Length + 1 > MaxLength)
            {
                break;
            }

            sb.Append(prefix).Append(entry);
            first = false;
        }

        return sb.ToString();
    }
}
