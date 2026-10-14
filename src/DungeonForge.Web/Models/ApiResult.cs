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

public sealed record ApiResult<T>(ApiStatus Status, T? Value = default, string? Error = null) where T : class
{
    public bool IsSuccess => Status == ApiStatus.Success;
}
