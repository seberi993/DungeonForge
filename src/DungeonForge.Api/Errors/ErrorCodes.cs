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
}
