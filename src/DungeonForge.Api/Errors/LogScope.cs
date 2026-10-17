namespace DungeonForge.Api.Errors;

// The scope every coded event is logged inside: which operation, which party and, when known, which turn.
public static class LogScope
{
    public static IDisposable? Begin(ILogger logger, string operation) =>
        logger.BeginScope(new Dictionary<string, object> { ["Operation"] = operation });

    public static IDisposable? Begin(ILogger logger, string operation, Guid partyId, int? turn = null)
    {
        var values = new Dictionary<string, object> { ["Operation"] = operation, ["PartyId"] = partyId };
        if (turn is { } t)
        {
            values["Turn"] = t;
        }

        return logger.BeginScope(values);
    }
}
