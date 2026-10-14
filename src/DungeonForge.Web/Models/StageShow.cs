namespace DungeonForge.Web.Models;

// One resolved turn to act out: the room it happened in, what each member did, and how it ended.
public sealed record StageShow(
    int Turn,
    RoomType Room,
    string? Scene,
    IReadOnlyList<ActionBeat> Beats,
    bool Success,
    string? TargetName,
    int Damage,
    int GoldFound);
