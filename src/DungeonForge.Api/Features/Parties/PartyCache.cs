using Microsoft.Extensions.Caching.Memory;

namespace DungeonForge.Api.Features.Parties;

internal static class PartyCache
{
    public static string Key(Guid id) => $"party:{id}";

    public static string InviteKey(string normalizedCode) => $"invite:{normalizedCode}";

    // Size is mandatory (cache SizeLimit); at the limit a Set may be dropped silently.
    public static MemoryCacheEntryOptions EntryOptions() => new()
    {
        Size = 1,
        SlidingExpiration = TimeSpan.FromHours(2),
    };
}
