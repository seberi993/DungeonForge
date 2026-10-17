using DungeonForge.Api.Errors;

namespace DungeonForge.Api.Features.Parties.ExploreDungeon;

// POST /explore is one route that does three things; each reports errors under its own operation.
public static class ExploreOperations
{
    public const string GetScenario = "Query:GetScenario";
    public const string LockIn = "Command:LockIn";
    public const string ResolveTurn = "Command:ResolveTurn";

    // Same branching as ExploreDungeonHandler.HandleAsync, for the request filter that runs before it.
    public static string For(ExploreRequest? request) => request?.Actions is not { Count: > 0 }
        ? GetScenario
        : request.SubmitOnly ? LockIn : ResolveTurn;
}

internal static class ExploreErrors
{
    private const string ActionsField = "actions";

    private static readonly ErrorSpec PartyDefeatedSpec = new(
        ErrorCodes.Turn.PartyDefeated, ErrorKind.Conflict, "Every member is defeated; the run is over.");

    // The four below were a 400 with errors.actions before and stay one: the Web client shows that message.
    private static readonly ErrorSpec ConfrontationInProgressSpec = new(
        ErrorCodes.Turn.ConfrontationInProgress, ErrorKind.Conflict,
        "A confrontation is in progress; settle the vote before the party moves on.", StatusCodes.Status400BadRequest, ActionsField);

    private static readonly ErrorSpec NoActiveEncounterSpec = new(
        ErrorCodes.Turn.NoActiveEncounter, ErrorKind.Conflict,
        "There is no active encounter to resolve.", StatusCodes.Status400BadRequest, ActionsField);

    private static readonly ErrorSpec ScenarioPendingSpec = new(
        ErrorCodes.Turn.ScenarioPending, ErrorKind.Conflict,
        "The next scenario is still being written; wait for it before acting.", StatusCodes.Status400BadRequest, ActionsField);

    private static readonly ErrorSpec NoActionsSpec = new(
        ErrorCodes.Turn.NoActions, ErrorKind.Validation, "At least one non-empty action is required.", Field: ActionsField);

    public static Error PartyDefeated(string operation, Guid partyId, int turn) =>
        PartyDefeatedSpec.Create(operation, Param.PartyId(partyId), Param.Turn(turn));

    public static Error ConfrontationInProgress(string operation, Guid partyId, int turn) =>
        ConfrontationInProgressSpec.Create(operation, Param.PartyId(partyId), Param.Turn(turn));

    public static Error NoActiveEncounter(string operation, Guid partyId, int turn) =>
        NoActiveEncounterSpec.Create(operation, Param.PartyId(partyId), Param.Turn(turn));

    public static Error ScenarioPending(string operation, Guid partyId, int turn) =>
        ScenarioPendingSpec.Create(operation, Param.PartyId(partyId), Param.Turn(turn));

    public static Error NoActions(string operation, Guid partyId) =>
        NoActionsSpec.Create(operation, Param.PartyId(partyId));
}
