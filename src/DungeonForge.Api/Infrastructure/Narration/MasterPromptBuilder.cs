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
        RoomType.Boss => "a boss fight",
        _ => "a treasure chest",
    };

    public static string Mood(bool wounded) => wounded ? "battered and weary" : "healthy and confident";

    // First-turn scenario request.
    public static string BuildScenario(NarrationContext ctx) =>
        Header(ctx, "Scene", ctx.Room) + Motifs(ctx) + RunFlavor.Describe(ctx.PartyName, ctx.Turn);

    // Resolve-turn request: result of the party's combined plan, the room it was for, and the room that follows.
    public static string Build(bool success, NarrationContext ctx)
    {
        var sb = new StringBuilder(Header(ctx, "Current scene", ctx.Room))
            .Append(" Result: the party's plan ").Append(success ? "succeeded" : "failed").Append('.');
        if (ctx.FloorCleared)
        {
            sb.Append(" The floor's boss has fallen and the party descends deeper.");
        }

        AppendNames(sb, " Members whose own action failed and who were hurt: ", ctx.Failed);
        AppendNames(sb, " Fallen heroes (describe them as down or dead): ", ctx.Downed);
        if (ctx.NextRoom is { } next)
        {
            sb.Append(" Next scene: ").Append(Scene(next)).Append('.');
        }

        sb.Append(Motifs(ctx));
        if (ctx.NextRoom is not null)
        {
            sb.Append(RunFlavor.Describe(ctx.PartyName, ctx.Turn));
        }

        AppendRoster(sb, ctx.Roster);
        AppendTreachery(sb, ctx.Treachery);

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

    public const int MaxMotifs = 3;
    public const int MaxMotifLength = 60;
    public const int MaxTreacheryEntries = 4;
    public const int MaxTreacheryLength = 160;

    // Injected set dressing (chosen in C# with variance) so the model stops reaching for stock dungeon scenery.
    private static string Motifs(NarrationContext ctx)
    {
        var motifs = ctx.Motifs.Take(MaxMotifs).Select(m => NarrationText.Clean(m, MaxMotifLength)).Where(m => m.Length > 0).ToArray();
        return motifs.Length == 0 ? string.Empty : $" Weave in at most two of these architectural elements: {string.Join("; ", motifs)}.";
    }

    // Names for targeted whispers; classes let the model address "the Mage" as well as "Merlin".
    private static void AppendRoster(StringBuilder sb, IReadOnlyList<RosterEntry> roster)
    {
        if (roster.Count == 0) return;
        sb.Append(" Roster: ")
            .AppendJoin(", ", roster.Select(r => $"{NarrationText.Sanitize(r.Name, MaxNameLength, NeutralMember)} ({r.Class})"))
            .Append('.');
    }

    // Covert plans already resolved in C#; the narrator only describes them as just exposed.
    private static void AppendTreachery(StringBuilder sb, IReadOnlyList<string> treachery)
    {
        var entries = treachery.Take(MaxTreacheryEntries).Select(t => NarrationText.Clean(t, MaxTreacheryLength)).Where(t => t.Length > 0).ToArray();
        if (entries.Length == 0) return;
        sb.Append(" Revealed treachery (secret plans just exposed; narrate them, never change them): ")
            .AppendJoin(" | ", entries)
            .Append('.');
    }

    private static void AppendNames(StringBuilder sb, string label, IReadOnlyList<string> names)
    {
        if (names.Count == 0) return;
        sb.Append(label)
            .AppendJoin(", ", names.Select(n => NarrationText.Sanitize(n, MaxNameLength, NeutralMember)))
            .Append('.');
    }

    private static string Header(NarrationContext ctx, string sceneLabel, RoomType room) =>
        $"Party: {NarrationText.Sanitize(ctx.PartyName, MaxNameLength, NeutralPartyName)}. Floor {ctx.Floor}. {sceneLabel}: {Scene(room)}. " +
        $"Mood: the party is {Mood(ctx.PartyWounded)}.";
}
