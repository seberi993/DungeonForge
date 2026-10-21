namespace DungeonForge.Application.Features.Parties.ExploreDungeon;

// What kind of text a NarrationBrief asks the browser's model for. The values are on the wire.
public static class NarrationKinds
{
    public const string Scenario = "scenario";
    public const string Turn = "turn";
}

// Where a turn's narration came from, as NarrationReady.Source reports it. The values are on the wire.
public static class NarrationSources
{
    public const string Server = "server";
    public const string Fallback = "fallback";
    public const string Model = "model";
}
