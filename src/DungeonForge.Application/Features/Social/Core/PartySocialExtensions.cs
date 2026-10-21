using DungeonForge.Domain;

namespace DungeonForge.Application.Features.Social.Core;

public static class PartySocialExtensions
{
    // The party's hidden social state. Callers lock party.SyncRoot while they use it.
    public static PartySocial Social(this Party party) => party.Extension((id, createdAt) => new PartySocial(RunSeedFor(id, createdAt)));

    private static ulong RunSeedFor(Guid id, DateTimeOffset createdAt)
    {
        Span<byte> bytes = stackalloc byte[16];
        id.TryWriteBytes(bytes);
        return SeededRng.Derive(
            BitConverter.ToUInt64(bytes[..8]) ^ BitConverter.ToUInt64(bytes[8..]), "run", (int)(createdAt.Ticks % int.MaxValue));
    }
}
