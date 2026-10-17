namespace DungeonForge.Api.Errors;

// Every error code, as `<area>.<reason>` constants in one nested class per area, e.g. Party.NotFound = "party.not_found".
// Add the constant, its ErrorSpec and a [CoversError] test together; ErrorCatalogTests fails otherwise.
public static class ErrorCodes;
