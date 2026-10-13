using System.Security.Cryptography;

namespace DungeonForge.Api.Features.Parties;

// Short, shareable party codes like "K7F2-9QXM". Alphabet omits look-alikes (0/O, 1/I).
internal static class InviteCodes
{
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private const int Length = 8;

    /// <summary>Returns a new random code in normalized form (no dash).</summary>
    public static string Generate() => RandomNumberGenerator.GetString(Alphabet, Length);

    /// <summary>Display form: XXXX-XXXX.</summary>
    public static string Format(string normalized) => $"{normalized[..4]}-{normalized[4..]}";

    /// <summary>Uppercases and strips dashes/whitespace; null when the input cannot be a code.</summary>
    public static string? Normalize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;

        var normalized = new string(input.Where(c => c != '-' && !char.IsWhiteSpace(c)).ToArray()).ToUpperInvariant();
        return normalized.Length == Length && normalized.All(Alphabet.Contains) ? normalized : null;
    }
}
