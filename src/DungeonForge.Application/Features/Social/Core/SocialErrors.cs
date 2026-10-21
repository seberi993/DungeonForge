using DungeonForge.Application.Errors;

namespace DungeonForge.Application.Features.Social.Core;

// Failures shared by the social slices. Messages never reveal hidden state (axes, other players' replies, ability numbers).
internal static class SocialErrors
{
    private static readonly ErrorSpec ModuleDisabledSpec = new(
        ErrorCodes.Social.ModuleDisabled, ErrorKind.NotFound, "This part of the game is switched off.");

    private static readonly ErrorSpec SeedAlreadySubmittedSpec = new(
        ErrorCodes.Social.SeedAlreadySubmitted, ErrorKind.Conflict, "You already answered the door's question.");

    // Field keeps the 400 body the Web client reads: errors.answer.
    private static readonly ErrorSpec SeedLowEffortSpec = new(
        ErrorCodes.Social.SeedLowEffort, ErrorKind.Validation, "Say a little more; the door wants a real answer.", Field: "answer");

    private static readonly ErrorSpec NoPeerPromptSpec = new(
        ErrorCodes.Social.NoPeerPrompt, ErrorKind.Conflict, "There is nothing to ask you about right now.");

    private static readonly ErrorSpec PeerPromptUnknownSpec = new(
        ErrorCodes.Social.PeerPromptUnknown, ErrorKind.NotFound, "That question is not waiting for your answer.");

    private static readonly ErrorSpec AsksUnavailableSpec = new(
        ErrorCodes.Social.AsksUnavailable, ErrorKind.Conflict, "Whispers need a party of more than three.");

    private static readonly ErrorSpec InvalidRecipientsSpec = new(
        ErrorCodes.Social.InvalidRecipients, ErrorKind.Validation,
        "Whisper to one to three other living members who are listening.", Field: "to");

    private static readonly ErrorSpec NotEnoughTokensSpec = new(
        ErrorCodes.Social.NotEnoughTokens, ErrorKind.Conflict, "You are out of whisper-shards.");

    private static readonly ErrorSpec AskCooldownSpec = new(
        ErrorCodes.Social.AskCooldown, ErrorKind.Conflict, "Too soon; let the last whisper settle.", Status: ErrorStatus.Status429TooManyRequests);

    private static readonly ErrorSpec TooManyOpenAsksSpec = new(
        ErrorCodes.Social.TooManyOpenAsks, ErrorKind.Conflict, "You already have two whispers waiting for answers.");

    private static readonly ErrorSpec AskNotFoundSpec = new(
        ErrorCodes.Social.AskNotFound, ErrorKind.NotFound, "That whisper has faded or never existed.");

    private static readonly ErrorSpec NotRecipientSpec = new(
        ErrorCodes.Social.NotRecipient, ErrorKind.Validation, "That whisper was not meant for you.",
        Status: ErrorStatus.Status403Forbidden, Field: "playerName");

    private static readonly ErrorSpec AlreadyRepliedSpec = new(
        ErrorCodes.Social.AlreadyReplied, ErrorKind.Conflict, "You already answered that whisper.");

    private static readonly ErrorSpec BluffForbiddenSpec = new(
        ErrorCodes.Social.BluffForbidden, ErrorKind.Validation, "You were pressed: answer honestly or dodge.", Field: "choice");

    private static readonly ErrorSpec NotEnoughCredibilitySpec = new(
        ErrorCodes.Social.NotEnoughCredibility, ErrorKind.Conflict, "Your word is worth too little for that.");

    private static readonly ErrorSpec NotAskerSpec = new(
        ErrorCodes.Social.NotAsker, ErrorKind.Validation, "Only the one who whispered can do that.",
        Status: ErrorStatus.Status403Forbidden, Field: "playerName");

    private static readonly ErrorSpec NothingToRevealSpec = new(
        ErrorCodes.Social.NothingToReveal, ErrorKind.Conflict, "There is nothing more to lay bare yet.");

    private static readonly ErrorSpec DuplicateAskSpec = new(
        ErrorCodes.Social.DuplicateAsk, ErrorKind.Conflict, "You already whispered something very much like that.");

    private static readonly ErrorSpec RunInProgressSpec = new(
        ErrorCodes.Social.RunInProgress, ErrorKind.Conflict, "The dungeon keeps its secrets until the run is over.");

    public static Error BluffForbidden(string operation, Guid partyId, string name) =>
        BluffForbiddenSpec.Create(operation, Param.PartyId(partyId), Param.PlayerName(name));

    public static Error NotEnoughCredibility(string operation, Guid partyId, string name) =>
        NotEnoughCredibilitySpec.Create(operation, Param.PartyId(partyId), Param.PlayerName(name));

    public static Error NotAsker(string operation, Guid partyId, string name) =>
        NotAskerSpec.Create(operation, Param.PartyId(partyId), Param.PlayerName(name));

    public static Error NothingToReveal(string operation, Guid partyId, string askId) =>
        NothingToRevealSpec.Create(operation, Param.PartyId(partyId), Param.Code(askId));

    public static Error DuplicateAsk(string operation, Guid partyId, string name) =>
        DuplicateAskSpec.Create(operation, Param.PartyId(partyId), Param.PlayerName(name));

    public static Error RunInProgress(string operation, Guid partyId) => RunInProgressSpec.Create(operation, Param.PartyId(partyId));

    public static Error ModuleDisabled(string operation, Guid partyId) => ModuleDisabledSpec.Create(operation, Param.PartyId(partyId));

    public static Error SeedAlreadySubmitted(string operation, Guid partyId, string name) =>
        SeedAlreadySubmittedSpec.Create(operation, Param.PartyId(partyId), Param.PlayerName(name));

    public static Error SeedLowEffort(string operation, Guid partyId) => SeedLowEffortSpec.Create(operation, Param.PartyId(partyId));

    public static Error NoPeerPrompt(string operation, Guid partyId, string name) =>
        NoPeerPromptSpec.Create(operation, Param.PartyId(partyId), Param.PlayerName(name));

    public static Error PeerPromptUnknown(string operation, Guid partyId, string name) =>
        PeerPromptUnknownSpec.Create(operation, Param.PartyId(partyId), Param.PlayerName(name));

    public static Error AsksUnavailable(string operation, Guid partyId) => AsksUnavailableSpec.Create(operation, Param.PartyId(partyId));

    public static Error InvalidRecipients(string operation, Guid partyId) => InvalidRecipientsSpec.Create(operation, Param.PartyId(partyId));

    public static Error NotEnoughTokens(string operation, Guid partyId, string name) =>
        NotEnoughTokensSpec.Create(operation, Param.PartyId(partyId), Param.PlayerName(name));

    public static Error AskCooldown(string operation, Guid partyId, string name) =>
        AskCooldownSpec.Create(operation, Param.PartyId(partyId), Param.PlayerName(name));

    public static Error TooManyOpenAsks(string operation, Guid partyId, string name) =>
        TooManyOpenAsksSpec.Create(operation, Param.PartyId(partyId), Param.PlayerName(name));

    public static Error AskNotFound(string operation, Guid partyId, string askId) =>
        AskNotFoundSpec.Create(operation, Param.PartyId(partyId), Param.Code(askId));

    public static Error NotRecipient(string operation, Guid partyId, string name) =>
        NotRecipientSpec.Create(operation, Param.PartyId(partyId), Param.PlayerName(name));

    public static Error AlreadyReplied(string operation, Guid partyId, string name) =>
        AlreadyRepliedSpec.Create(operation, Param.PartyId(partyId), Param.PlayerName(name));
}
