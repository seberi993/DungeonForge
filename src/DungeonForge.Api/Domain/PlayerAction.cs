namespace DungeonForge.Api.Domain;

// One member's free-text tactical input for the current encounter. Text is sanitized before it is stored.
public sealed record PlayerAction(Guid MemberId, string Name, string Text);
