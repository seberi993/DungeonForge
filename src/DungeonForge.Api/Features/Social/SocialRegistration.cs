using DungeonForge.Api.Features.Social.Abilities;
using DungeonForge.Api.Features.Social.Asks.ReplyAsk;
using DungeonForge.Api.Features.Social.Asks.RevealAsk;
using DungeonForge.Api.Features.Social.Asks.SendAsk;
using DungeonForge.Api.Features.Social.Core;
using DungeonForge.Api.Features.Social.Core.GetDebrief;
using DungeonForge.Api.Features.Social.Core.GetMySocial;
using DungeonForge.Api.Features.Social.Core.SetPreferences;
using DungeonForge.Api.Features.Social.Peers;
using DungeonForge.Api.Features.Social.Peers.GetPeerPrompt;
using DungeonForge.Api.Features.Social.Peers.SubmitPeerAnswer;
using DungeonForge.Api.Features.Social.Seed;
using DungeonForge.Api.Features.Social.Seed.FinalizeProfiles;
using DungeonForge.Api.Features.Social.Seed.SubmitSeed;
using DungeonForge.Api.Features.Social.Supervision;
using DungeonForge.Api.Infrastructure.Narration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

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
        services.Configure<SocialModelOptions>(configuration.GetSection("Social:Model"));
        services.TryAddSingleton<IKeyPool, KeyPool>();
        services.AddHttpClient<GeminiJsonModel>((sp, http) =>
            http.BaseAddress = new Uri(sp.GetRequiredService<IOptions<NarrationOptions>>().Value.BaseUrl));
        services.TryAddSingleton<IJsonModel>(sp => sp.GetRequiredService<GeminiJsonModel>());
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
