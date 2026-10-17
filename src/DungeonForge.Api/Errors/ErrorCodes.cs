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


    public static class Validation
    {
        public const string InvalidRequest = "validation.invalid_request";
    }
}
