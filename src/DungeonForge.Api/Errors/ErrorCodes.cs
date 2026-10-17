namespace DungeonForge.Api.Errors;

// Every error code, as `<area>.<reason>` constants in one nested class per area, e.g. Party.NotFound = "party.not_found".
// Add the constant, its ErrorSpec and a [CoversError] test together; ErrorCatalogTests fails otherwise.
public static class ErrorCodes
{
    public static class Unexpected
    {
        public const string Unhandled = "unexpected.unhandled";
    }

    public static class Request
    {
        public const string Malformed = "request.malformed";
    }

    public static class Party
    {
        public const string NotFound = "party.not_found";
        public const string InviteNotFound = "party.invite_not_found";
        public const string NameTaken = "party.name_taken";
        public const string Full = "party.full";
        public const string PlayerUnknown = "party.player_unknown";
    }

    public static class Vault
    {
        public const string NoVault = "vault.no_vault";
        public const string ConfrontationActive = "vault.confrontation_active";
    }

    public static class Vote
    {
        public const string NoConfrontation = "vote.no_confrontation";
        public const string NotEligible = "vote.not_eligible";
    }

    public static class Turn
    {
        public const string PartyDefeated = "turn.party_defeated";
        public const string ConfrontationInProgress = "turn.confrontation_in_progress";
        public const string NoActiveEncounter = "turn.no_active_encounter";
        public const string ScenarioPending = "turn.scenario_pending";
        public const string NoActions = "turn.no_actions";
    }

    public static class Narration
    {
        public const string NotAwaited = "narration.not_awaited";
        public const string TextTooLong = "narration.text_too_long";
    }

    public static class Extract
    {
        public const string NoPact = "extract.no_pact";
    }

    public static class Social
    {
        public const string ModuleDisabled = "social.module_disabled";
        public const string SeedAlreadySubmitted = "social.seed_already_submitted";
        public const string SeedLowEffort = "social.seed_low_effort";
        public const string NoPeerPrompt = "social.no_peer_prompt";
        public const string PeerPromptUnknown = "social.peer_prompt_unknown";
        public const string AsksUnavailable = "social.asks_unavailable";
        public const string InvalidRecipients = "social.invalid_recipients";
        public const string NotEnoughTokens = "social.not_enough_tokens";
        public const string AskCooldown = "social.ask_cooldown";
        public const string TooManyOpenAsks = "social.too_many_open_asks";
        public const string AskNotFound = "social.ask_not_found";
        public const string NotRecipient = "social.not_recipient";
        public const string AlreadyReplied = "social.already_replied";
    }


    public static class Validation
    {
        public const string InvalidRequest = "validation.invalid_request";
    }
}
