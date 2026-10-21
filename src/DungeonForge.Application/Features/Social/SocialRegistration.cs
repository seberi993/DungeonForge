using DungeonForge.Application.Features.Social.Abilities;
using DungeonForge.Application.Features.Social.Asks.ReplyAsk;
using DungeonForge.Application.Features.Social.Asks.RevealAsk;
using DungeonForge.Application.Features.Social.Asks.SendAsk;
using DungeonForge.Application.Features.Social.Core;
using DungeonForge.Application.Features.Social.Core.GetDebrief;
using DungeonForge.Application.Features.Social.Core.GetMySocial;
using DungeonForge.Application.Features.Social.Core.SetPreferences;
using DungeonForge.Application.Features.Social.Peers;
using DungeonForge.Application.Features.Social.Peers.GetPeerPrompt;
using DungeonForge.Application.Features.Social.Peers.SubmitPeerAnswer;
using DungeonForge.Application.Features.Social.Seed;
using DungeonForge.Application.Features.Social.Seed.FinalizeProfiles;
using DungeonForge.Application.Features.Social.Seed.SubmitSeed;
using DungeonForge.Application.Features.Social.Supervision;
using DungeonForge.Application.Narration;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DungeonForge.Application.Features.Social;

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
        services.Configure<SocialModelOptions>(configuration.GetSection("Social:Model"));
        // Model-backed parser; it falls back to the keyword rules on any failure (and when no key is configured).
        services.TryAddSingleton<IAxisParser, GeminiAxisParser>();
        services.AddSingleton<SubmitSeedHandler>();
        services.AddSingleton<FinalizeProfilesHandler>();

        // Peers module
        services.AddSingleton<GetPeerPromptHandler>();
        services.AddSingleton<SubmitPeerAnswerHandler>();

        // Asks module
        services.AddSingleton<SendAskHandler>();
        services.AddSingleton<ReplyAskHandler>();
        services.AddSingleton<RevealAskHandler>();

        // Supervision module: rules are independent; the first (by Order) to claim an event decides it.
        services.AddSingleton<ISocialRule, UnanimousPeerRule>();
        services.AddSingleton<ISocialRule, TheftClaimRule>();
        services.AddSingleton<ISocialRule, SubgroupConsensusRule>();
        services.TryAddSingleton<ISocialSupervisor, RuleSupervisor>();
        services.TryAddSingleton<SocialPipeline>();
        services.TryAddSingleton<NarratorJudgeQueue>();
        services.TryAddSingleton<NarratorJudge>();
        services.AddHostedService<NarratorJudgeWorker>();

        // Abilities module
        services.TryAddSingleton<SocialTurnPipeline>();

        services.AddSingleton<GetMySocialHandler>();
        services.AddSingleton<SetPreferencesHandler>();
        services.AddSingleton<GetDebriefHandler>();
        return services;
    }
}
