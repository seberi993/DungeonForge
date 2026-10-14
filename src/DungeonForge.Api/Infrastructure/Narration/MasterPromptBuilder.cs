using System.Text;
using DungeonForge.Api.Features.Parties.ExploreDungeon;

namespace DungeonForge.Api.Infrastructure.Narration;

// Composes ONE user prompt per call from the room, party status, the resolved result, the next room and every member's input.
public static class MasterPromptBuilder
{
    public const int MaxLength = 2000;
    public const int MaxActionTextLength = 200;
    public const int MaxNameLength = 40;

    private const string NeutralPartyName = "the party";
    private const string NeutralMember = "A member";

    public static string Scene(RoomType room) => room switch
    {
        RoomType.Combat => "a combat encounter",
        RoomType.Puzzle => "a puzzle room",
        _ => "a treasure chest",
    };

    public static string Mood(bool wounded) => wounded ? "battered and weary" : "healthy and confident";

    // First-turn scenario request.
    public static string BuildScenario(NarrationContext ctx) =>
        Header(ctx, "Scene", ctx.Room);

    // Resolve-turn request: result of the party's combined plan, the room it was for, and the room that follows.
    public static string Build(bool success, NarrationContext ctx)
    {
        var sb = new StringBuilder(Header(ctx, "Current scene", ctx.Room))
            .Append(" Result: the party's plan ").Append(success ? "succeeded" : "failed").Append('.');
        if (ctx.NextRoom is { } next)
        {
            sb.Append(" Next scene: ").Append(Scene(next)).Append('.');
        }

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

    private static string Header(NarrationContext ctx, string sceneLabel, RoomType room) =>
        $"Party: {NarrationText.Sanitize(ctx.PartyName, MaxNameLength, NeutralPartyName)}. {sceneLabel}: {Scene(room)}. " +
        $"Mood: the party is {Mood(ctx.PartyWounded)}.";
}
