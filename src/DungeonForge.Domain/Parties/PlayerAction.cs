namespace DungeonForge.Domain;

// One member's free-text tactical input for the current encounter. Text is sanitized before it is stored.
// Covert is the optional hidden intention (backstab, sabotage, steal); it is only ever shown after the turn resolves.
public sealed record PlayerAction(Guid MemberId, string Name, string Text, CovertIntent? Covert = null);
