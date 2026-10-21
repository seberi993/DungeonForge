namespace DungeonForge.Application.Features.Social.Asks;

public enum AskKind
{
    Question,
    Accusation,
    Offer,
    Plea,
}

public enum AskChoice
{
    Honest,
    Dodge,
    Bluff,
}

public sealed record AskReply(string By, AskChoice Choice, string? Text, DateTimeOffset At);

// Ephemeral private channel. Body is purged on expiry; only the audit journal keeps (hashed) metadata.
public sealed class AskThread(string id, string from, IReadOnlyList<string> to, string text, AskKind kind, DateTimeOffset createdAt, DateTimeOffset expiresAt)
{
    public const int MaxTextLength = 140;
    public const int MaxReplyLength = 80;
    public const int MaxRecipients = 3;
    public const int MaxOpenPerPlayer = 2;
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan SenderCooldown = TimeSpan.FromSeconds(45);
    public static readonly TimeSpan PairCooldown = TimeSpan.FromSeconds(90);

    // A near-copy of an earlier whisper from the same sender inside this window is refused.
    public static readonly TimeSpan DuplicateWindow = TimeSpan.FromMinutes(5);

    public string Id { get; } = id;

    // The asker pressed the recipients (cost them credibility): they may only answer honestly or dodge.
    public bool Compelled { get; init; }

    // The asker laid this whisper and its answers bare to the whole party.
    public bool Revealed { get; set; }

    public string From { get; } = from;

    public IReadOnlyList<string> To { get; } = to;

    public string Text { get; private set; } = text;

    public AskKind Kind { get; } = kind;

    public DateTimeOffset CreatedAt { get; } = createdAt;

    public DateTimeOffset ExpiresAt { get; } = expiresAt;

    public Dictionary<string, AskReply> Replies { get; } = new(StringComparer.OrdinalIgnoreCase);

    public bool Purged { get; private set; }

    public bool IsExpired(DateTimeOffset now) => Purged || now >= ExpiresAt;

    public bool Involves(string name) =>
        string.Equals(From, name, StringComparison.OrdinalIgnoreCase) || To.Contains(name, StringComparer.OrdinalIgnoreCase);

    public void Purge()
    {
        Purged = true;
        Text = string.Empty;
        Replies.Clear();
    }
}

public static class AskText
{
    // Word-set overlap: "who has the key?" and "Who has the KEY" are the same whisper; reordered filler is too.
    public static bool Similar(string a, string b)
    {
        var x = Words(a);
        var y = Words(b);
        if (x.Count == 0 || y.Count == 0)
        {
            return false;
        }

        return x.Intersect(y).Count() / (double)x.Union(y).Count() > 0.8;
    }

    private static HashSet<string> Words(string text) =>
        [.. text.ToLowerInvariant().Split([' ', '.', ',', '!', '?', ';', ':', '"', '\''], StringSplitOptions.RemoveEmptyEntries)];
}
