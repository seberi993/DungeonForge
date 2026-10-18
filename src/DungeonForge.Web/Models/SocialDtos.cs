namespace DungeonForge.Web.Models;

// Mirrors the server's /social endpoints. Everything here is flavor: tags, words and names. The server never sends axis
// values, ability budgets or cooldown numbers, so there is nothing numeric to show.
public sealed record AbilityDto(string Id, string Name, string Flavor, string Hint, bool Ready);

public sealed record InboxAskDto(string AskId, string From, string Kind, string Text, DateTimeOffset ExpiresAt, bool Replied);

public sealed record SentReplyDto(string By, string Summary);

public sealed record SentAskDto(string AskId, IReadOnlyList<string> To, string Kind, string Text, DateTimeOffset ExpiresAt, IReadOnlyList<SentReplyDto> Replies);

public sealed record MySocialDto(
    string PlayerName,
    string? SeedQuestion,
    bool Seeded,
    bool Finalized,
    IReadOnlyList<string> Tags,
    string Standing,
    IReadOnlyList<AbilityDto> Abilities,
    int AskTokens,
    bool AsksAvailable,
    IReadOnlyList<InboxAskDto> Inbox,
    IReadOnlyList<SentAskDto> Sent,
    bool OptedOut = false,
    bool DebriefOptIn = false);

public sealed record PeerPromptDto(string PromptId, string Question, string About, IReadOnlyList<string> Options, int Remaining);

public sealed record SubmitSeedRequest(string PlayerName, string Answer);

public sealed record SeedResultDto(string PlayerName, IReadOnlyList<string> Tags, bool Finalized, int Waiting);

public sealed record FinalizeRequest(string PlayerName);

public sealed record PeerAnswerRequest(string PlayerName, string PromptId, string Choice);

public sealed record SendAskRequest(string From, IReadOnlyList<string> To, string Text, string Kind, bool Compel = false);

public sealed record RevealAskRequest(string From);

public sealed record PreferencesRequest(string PlayerName, bool? OptOut = null, bool? DebriefOptIn = null);

public sealed record ReplyAskRequest(string From, string Choice, string? Text);

public sealed record AcceptedDto(bool Accepted = true);

public sealed record PreferencesDto(bool OptOut, bool DebriefOptIn);

// Sent beside the actions, never inside them: the choice of gift stays private to the server.
public sealed record AbilityIntent(Guid MemberId, string AbilityId);

// The post-run debrief: exact numbers, shown only when the run is over (and only the player's own, when they opted in).
public sealed record DebriefAbilityDto(string Name, string Archetype, string Rarity, int Budget, int Cooldown, IReadOnlyDictionary<string, int> Effects);

public sealed record DebriefMomentDto(int Turn, string Type, string Verdict, string Rule, string Rationale, bool Model);

public sealed record DebriefDto(
    string PlayerName,
    bool OptedIn,
    IReadOnlyDictionary<string, int>? Axes,
    IReadOnlyList<string> Tags,
    IReadOnlyList<string> Tone,
    int? Trust,
    int? Menace,
    int? Credibility,
    IReadOnlyList<DebriefAbilityDto>? Abilities,
    IReadOnlyList<DebriefMomentDto>? Moments,
    IReadOnlyDictionary<string, int> Run);
