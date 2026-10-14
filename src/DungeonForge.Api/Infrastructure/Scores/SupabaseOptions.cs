namespace DungeonForge.Api.Infrastructure.Scores;

public sealed class SupabaseOptions
{
    // https://<project>.supabase.co
    public string? Url { get; set; }

    // Service-role key; server-side only, supply via user secrets or env (Supabase__ServiceKey).
    public string? ServiceKey { get; set; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Url) && !string.IsNullOrWhiteSpace(ServiceKey);
}
