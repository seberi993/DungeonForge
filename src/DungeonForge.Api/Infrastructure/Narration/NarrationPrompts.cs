using DungeonForge.Api.Features.Parties.ExploreDungeon;

namespace DungeonForge.Api.Infrastructure.Narration;

// One system prompt per room type. Narration is flavor only: game rules and math stay in C#.
public static class NarrationPrompts
{
    private const string Persona =
        "You are the narrator of a dark-fantasy dungeon crawl for a cooperative party. ";

    private const string ScenarioShape =
        "about 120 to 180 words in second person plural covering the setting, atmosphere, concrete hazards and features, " +
        "the stakes, and clear hooks the party can act on. Do not offer a menu of actions; the party must think, plan and " +
        "discuss. End with the open question: What do you do?";

    private const string Rules =
        "Do not include any number, statistic, rule, or reward; never change the stated result.";

    public static string RoomGuidance(RoomType room) => room switch
    {
        RoomType.Combat =>
            "Scene type: combat. Introduce a concrete group of enemies with distinct behaviors, the terrain (cover, " +
            "chokepoints, hazards), and what the enemies want, so the party can plan tactics and roles.",
        RoomType.Puzzle =>
            "Scene type: puzzle room. Describe a strange mechanism with clues in glyphs, symbols, statues or the " +
            "environment, a looming consequence, and several parts to interact with, so the party can reason together.",
        _ =>
            "Scene type: treasure chest. Describe the chest, its surroundings, the lock or fastening, signs of traps or " +
            "guardians, and tempting but risky details, so the party can weigh how to open it.",
    };

    // First turn: plain prose scenario.
    public static string Scenario(RoomType room) =>
        $"{Persona}Write ONE scenario of {ScenarioShape} {RoomGuidance(room)} {Rules} Reply with plain prose only, no markdown.";

    // Every later turn: strict JSON with the outcome and (unless the party fell) the next scenario.
    public static string Turn(RoomType? next) => next is { } room
        ? $"{Persona}Reply with strict JSON only, no markdown, each string on a single line, in the shape " +
          "{\"outcome\":\"...\",\"next\":\"...\"}. " +
          "\"outcome\": 2 to 4 vivid sentences describing how the party's combined plan played out, matching the stated " +
          "result exactly; weave in the member ideas that fit, in the order given. " +
          $"\"next\": a NEW scenario of {ScenarioShape} {RoomGuidance(room)} {Rules}"
        : $"{Persona}Reply with strict JSON only, no markdown, each string on a single line, in the shape " +
          "{\"outcome\":\"...\"}. " +
          "\"outcome\": 2 to 4 vivid sentences describing how the party's combined plan played out, matching the stated " +
          "result exactly; weave in the member ideas that fit, in the order given. " + Rules;
}
