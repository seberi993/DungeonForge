using DungeonForge.Api.Features.Social.Abilities;
using DungeonForge.Api.Features.Social.Asks.ReplyAsk;
using DungeonForge.Api.Features.Social.Asks.SendAsk;
using DungeonForge.Api.Features.Social.Core;
using DungeonForge.Api.Features.Social.Core.GetMySocial;
using DungeonForge.Api.Features.Social.Peers;
using DungeonForge.Api.Features.Social.Peers.GetPeerPrompt;
using DungeonForge.Api.Features.Social.Peers.SubmitPeerAnswer;
using DungeonForge.Api.Features.Social.Seed;
using DungeonForge.Api.Features.Social.Seed.FinalizeProfiles;
using DungeonForge.Api.Features.Social.Seed.SubmitSeed;
using DungeonForge.Api.Features.Social.Supervision;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DungeonForge.Api.Features.Social;

// One place that wires the social game. Each module is a block: delete it (and its folder) to remove the feature, or add a
// new ISocialRule / IAxisParser registration to extend one. Config section "Social" switches modules off without a rebuild.
public static class SocialRegistration
{
    public static IServiceCollection AddSocial(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<SocialOptions>(configuration.GetSection("Social"));
        services.Configure<PeerParams>(configuration.GetSection("Social:Peers"));
        services.TryAddSingleton<SocialModules>();
        services.TryAddSingleton<SocialAccess>();
        services.TryAddSingleton<SocialDelivery>();
        services.TryAddSingleton(new AbilityGenerator());
        services.TryAddSingleton<SocialLifecycle>();

        // Seed module
        services.TryAddSingleton<IAxisParser, KeywordAxisParser>();
        services.AddSingleton<SubmitSeedHandler>();
        services.AddSingleton<FinalizeProfilesHandler>();

        // Peers module
        services.AddSingleton<GetPeerPromptHandler>();
        services.AddSingleton<SubmitPeerAnswerHandler>();

        // Asks module
        services.AddSingleton<SendAskHandler>();
        services.AddSingleton<ReplyAskHandler>();

        // Supervision module: rules are independent; the first (by Order) to claim an event decides it.
        services.AddSingleton<ISocialRule, UnanimousPeerRule>();
        services.AddSingleton<ISocialRule, TheftClaimRule>();
        services.AddSingleton<ISocialRule, SubgroupConsensusRule>();
        services.TryAddSingleton<ISocialSupervisor, RuleSupervisor>();
        services.TryAddSingleton<SocialPipeline>();

        // Abilities module
        services.TryAddSingleton<SocialTurnPipeline>();

        services.AddSingleton<GetMySocialHandler>();
        return services;
    }
}
