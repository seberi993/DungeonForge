namespace DungeonForge.Web.Models;

public enum RoomType
{
    Combat,
    Chest,
    Puzzle
}

public enum CharacterClass
{
    Warrior,
    Mage,
    Druid,
    Paladin,
    Priest,
    Necromancer,
    Bard
}

public sealed record MemberRequest(string Name, CharacterClass Class);

public sealed record CreatePartyRequest(string Name, IReadOnlyList<MemberRequest> Members);

public sealed record MemberDto(string Name, CharacterClass Class, int Hp, int MaxHp, bool IsDefeated);

public sealed record PartyResponse(Guid PartyId, string Name, IReadOnlyList<MemberDto> Members, DateTimeOffset CreatedAt, string InviteCode = "");

public sealed record ExploreResponse(
    Guid PartyId,
    int Turn,
    RoomType RoomType,
    string? TargetName,
    int Damage,
    int GoldFound,
    string FlavorEvent,
    int Gold,
    bool IsPartyDefeated,
    string? LastOutcome = null,
    IReadOnlyList<PlayerAction>? Discussion = null,
    bool NarrationPending = false,
    string? Scenario = null,
    bool ScenarioPending = false,
    IReadOnlyList<LockStatusDto>? LockStatus = null,
    IReadOnlyList<ActionBeat>? Beats = null,
    bool Success = false,
    IReadOnlyList<HitDto>? Hits = null,
    NarrationBrief? Brief = null);

// Prompts for the browser's local model; sent only to the request that claimed the narration slot.
public sealed record NarrationBrief(int Turn, string Kind, string System, string User, string? JsonSchema, int MaxTokens);

public sealed record SubmitNarrationRequest(int Turn, string? Text, bool? Declined);

// One member's own damage from a failed action; TargetName/Damage on the response mirror the first hit.
public sealed record HitDto(string Name, int Damage);

public enum ActionVerb
{
    Strike,
    Cast,
    Stab,
    Heal
}

// One member’s on-screen action for a resolved turn; the stage plays these in order.
public sealed record ActionBeat(string Name, CharacterClass Class, ActionVerb Verb, string Text);

public sealed record PlayerAction(Guid MemberId, string Name, string Text);

public sealed record NarrationReady(Guid PartyId, int Turn, string Outcome, string? NextScenario = null, RoomType? NextRoomType = null, string? NextScene = null, string? Source = null);

public sealed record ExploreRequest(IReadOnlyList<PlayerAction>? Actions = null, bool? SubmitOnly = null);

public sealed record LogEntry(int Turn, RoomType RoomType, string? TargetName, int Damage, int GoldFound, string FlavorEvent, string? Outcome = null, string? Scenario = null);

public sealed record PartyStateDto(
    Guid PartyId,
    string Name,
    IReadOnlyList<MemberDto> Members,
    DateTimeOffset CreatedAt,
    int Gold,
    int Turn,
    bool IsPartyDefeated,
    string InviteCode = "",
    IReadOnlyList<LockStatusDto>? LockStatus = null);

public sealed record LockStatusDto(string Name, bool LockedIn);

public sealed record JoinPartyRequest(string Name, CharacterClass Class);

public sealed record JoinPartyResponse(string MemberName, PartyStateDto State);

public sealed record UnlockRequest(Guid MemberId);

public sealed record UnlockResponse(Guid PartyId, IReadOnlyList<LockStatusDto> LockStatus);

public enum ElementType
{
    Physical,
    Fire,
    Ice,
    Holy,
    Unholy
}

public sealed record SpellDto(string Name, CharacterClass ClassId, ElementType Element, int ManaCost, string Description);
