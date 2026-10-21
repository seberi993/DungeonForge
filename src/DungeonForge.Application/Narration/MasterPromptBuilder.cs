using System.Text;
using DungeonForge.Domain;
using DungeonForge.Shared;

namespace DungeonForge.Application.Narration;

// Composes ONE user prompt per call from the room, party status, the resolved result, the next room and every member's input.
public static class MasterPromptBuilder
{
    public const int MaxLength = 2000;
    public const int MaxActionTextLength = Limits.MaxActionTextLength;
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
        Header(ctx, "Scene", ctx.Room) + Motifs(ctx) + RunFlavor.Describe(ctx.PartyName, ctx.Turn) + PlayerCanon(ctx, ScenarioCanonBudget) + Leanings(ctx);

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
        sb.Append(PlayerCanon(ctx, TurnCanonBudget)).Append(Leanings(ctx)).Append(RecentOutcomes(ctx));

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

    public const int ScenarioCanonBudget = 1200;
    public const int TurnCanonBudget = 500;
    public const int MaxRecentOutcomes = 3;
    public const int MaxRecentOutcomeLength = 120;

    // What the players painted in the opening scene: canon the DM builds on for the whole run, trimmed to a budget.
    private static string PlayerCanon(NarrationContext ctx, int budget)
    {
        var sb = new StringBuilder();
        foreach (var answer in ctx.SetupAnswers)
        {
            var text = NarrationText.Clean(answer.Text, Limits.MaxSetupAnswerLength);
            if (text.Length == 0) continue;

            var entry = $"{NarrationText.Sanitize(answer.Name, MaxNameLength, NeutralMember)}: {text}";
            if (sb.Length + entry.Length + 3 > budget) break;

            sb.Append(sb.Length == 0 ? string.Empty : " | ").Append(entry);
        }

        return sb.Length == 0 ? string.Empty : $" The players painted this world in their own words (canon; build on it, never contradict it): {sb}.";
    }

    // The DM's last few lines, so the next one does not echo their wording, imagery or opening words.
    private static string RecentOutcomes(NarrationContext ctx)
    {
        var recent = ctx.RecentOutcomes.TakeLast(MaxRecentOutcomes).Select(o => NarrationText.Clean(o, MaxRecentOutcomeLength)).Where(o => o.Length > 0).ToArray();
        return recent.Length == 0 ? string.Empty : $" Already told (do not repeat their words or images): {string.Join(" | ", recent)}.";
    }

    private static string Leanings(NarrationContext ctx)
    {
        var tags = ctx.StoryTags.Take(3).Select(t => NarrationText.Clean(t, MaxMotifLength)).Where(t => t.Length > 0).ToArray();
        return tags.Length == 0 ? string.Empty : $" The party keeps turning out to be: {string.Join("; ", tags)}. Tempt or test that.";
    }

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
