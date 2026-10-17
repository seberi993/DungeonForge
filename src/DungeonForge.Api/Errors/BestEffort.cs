namespace DungeonForge.Api.Errors;

// A send whose failure must never fail the request (a hub broadcast, a private message). Failures are logged once as
// LogEvents.BroadcastFailed inside the operation/party(/turn) scope and swallowed.
public static class BestEffort
{
    public static async Task RunAsync(
        ILogger logger, string operation, Guid partyId, string what, Func<Task> send, int? turn = null)
    {
        using var scope = LogScope.Begin(logger, operation, partyId, turn);
        try
        {
            await send();
        }
        catch (Exception ex)
        {
            logger.BroadcastFailed(ex, what);
        }
    }
}
