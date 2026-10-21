namespace DungeonForge.Application.Errors;

// HTTP status numbers an ErrorSpec maps to. Plain ints so Application never references ASP.NET; the Api edge sends them as-is.
public static class ErrorStatus
{
    public const int Status400BadRequest = 400;
    public const int Status403Forbidden = 403;
    public const int Status404NotFound = 404;
    public const int Status409Conflict = 409;
    public const int Status429TooManyRequests = 429;
    public const int Status500InternalServerError = 500;
}
