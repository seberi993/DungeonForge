namespace DungeonForge.Web.Features.Dungeon;

// Where a turn's narration came from, as the server reports it in NarrationReady.Source (the Api's NarrationSources). The
// values are on the wire.
public static class NarrationSources
{
    public const string Server = "server";
    public const string Fallback = "fallback";
    public const string Model = "model";
}
