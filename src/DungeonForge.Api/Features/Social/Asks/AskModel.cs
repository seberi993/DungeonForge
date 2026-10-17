namespace DungeonForge.Api.Features.Social.Asks;

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

    public string Id { get; } = id;

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
