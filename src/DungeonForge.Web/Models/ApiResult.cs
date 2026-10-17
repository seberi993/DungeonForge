namespace DungeonForge.Web.Models;

public enum ApiStatus
{
    Success,
    ValidationError,
    NotFound,
    Conflict,
    Waiting,
    Failed
}

// Error is the message to show a player. Code, Description and Parameters are the server's ProblemDetails fields
// (`code`, `detail`, `parameters`): null on success, on the waiting 409, and when the server sent no problem body.
public sealed record ApiResult<T>(
    ApiStatus Status,
    T? Value = default,
    string? Error = null,
    string? Code = null,
    string? Description = null,
    IReadOnlyDictionary<string, string>? Parameters = null) where T : class
{
    public bool IsSuccess => Status == ApiStatus.Success;
}
