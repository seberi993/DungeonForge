using Microsoft.Extensions.Options;

namespace DungeonForge.Application.Features.Social.Core;

// Each module can be switched off independently (config section "Social"); a disabled module's endpoints answer 404
// and its hooks do nothing, so rules and features can be added or removed without touching the others.
public enum SocialModule
{
    Seed,
    Peers,
    Abilities,
    Asks,
    Supervision,
}

public sealed class SocialOptions
{
    public bool Enabled { get; set; } = true;

    public bool Seed { get; set; } = true;

    public bool Peers { get; set; } = true;

    public bool Abilities { get; set; } = true;

    public bool Asks { get; set; } = true;

    public bool Supervision { get; set; } = true;

    // Private coordination needs more than three players: with three or fewer, a whisper would exclude exactly one person.
    public int MinPlayersForAsks { get; set; } = 4;
}

public sealed class SocialModules(IOptions<SocialOptions> options)
{
    public bool IsEnabled(SocialModule module) => options.Value.Enabled && module switch
    {
        SocialModule.Seed => options.Value.Seed,
        SocialModule.Peers => options.Value.Peers,
        SocialModule.Abilities => options.Value.Abilities,
        SocialModule.Asks => options.Value.Asks,
        SocialModule.Supervision => options.Value.Supervision,
        _ => false,
    };

    public bool Enabled => options.Value.Enabled;

    public int MinPlayersForAsks => options.Value.MinPlayersForAsks;
}
