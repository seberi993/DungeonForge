using DungeonForge.Api.Errors;

namespace DungeonForge.Api.Features.Parties.SubmitNarration;

public static class NarrationErrors
{
    private static readonly ErrorSpec NotAwaitedSpec = new(
        ErrorCodes.Narration.NotAwaited, ErrorKind.Conflict,
        "No narration is awaited for that turn: it was already submitted, or the server narrator took over.");

    // Field keeps today's 400 body: errors.text.
    private static readonly ErrorSpec TextTooLongSpec = new(
        ErrorCodes.Narration.TextTooLong, ErrorKind.Validation,
        $"Text must be at most {SubmitNarrationLimits.MaxTextLength} characters.", Field: "text");

    public static Error NotAwaited(string operation, Guid partyId, int turn) =>
        NotAwaitedSpec.Create(operation, Param.PartyId(partyId), Param.Turn(turn));

    public static Error TextTooLong(string operation, Guid partyId, int turn) =>
        TextTooLongSpec.Create(operation, Param.PartyId(partyId), Param.Turn(turn));
}
