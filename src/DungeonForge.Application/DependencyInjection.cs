using DungeonForge.Application.Features.Parties;
using DungeonForge.Application.Features.Parties.CastConfrontationVote;
using DungeonForge.Application.Features.Parties.Chaos;
using DungeonForge.Application.Features.Parties.ClaimVault;
using DungeonForge.Application.Features.Parties.CreateParty;
using DungeonForge.Application.Features.Parties.ExploreDungeon;
using DungeonForge.Application.Features.Parties.ExtractRun;
using DungeonForge.Application.Features.Parties.GetPartyState;
using DungeonForge.Application.Features.Parties.GetSetup;
using DungeonForge.Application.Features.Parties.JoinByCode;
using DungeonForge.Application.Features.Parties.JoinParty;
using DungeonForge.Application.Features.Parties.Realtime;
using DungeonForge.Application.Features.Parties.SubmitSetup;
using DungeonForge.Application.Features.Parties.UnlockTurn;
using DungeonForge.Application.Features.Scores.GetHighScores;
using DungeonForge.Application.Features.Social;
using DungeonForge.Application.Narration;
using DungeonForge.Domain;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DungeonForge.Application;

public static class DependencyInjection
{
    // Every handler is a singleton: they hold no state of their own, party state lives behind IPartyStore.
    public static IServiceCollection AddApplication(this IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IRoller, RandomRoller>();
        // Chaos systems own their roller so their variance never disturbs the deterministic game-roll order.
        services.TryAddSingleton(sp => new ChaosDispatcher(
            sp.GetRequiredService<IPartyNotifier>(), new RandomRoller(), sp.GetRequiredService<ILogger<ChaosDispatcher>>()));
        services.TryAddSingleton(new VocabularySelector(new RandomRoller()));

        // Parties
        services.AddSingleton<CreatePartyHandler>();
        services.AddSingleton<GetPartyStateHandler>();
        services.AddSingleton<GetSetupHandler>();
        services.AddSingleton<SubmitSetupHandler>();
        services.AddSingleton<JoinByCodeHandler>();
        services.AddSingleton<JoinPartyHandler>();
        services.AddSingleton<UnlockTurnHandler>();
        services.AddSingleton<ClaimVaultHandler>();
        services.AddSingleton<CastConfrontationVoteHandler>();
        services.AddSingleton<ExtractRunHandler>();

        // The turn
        services.AddSingleton<TurnNarrator>();
        services.AddSingleton<ClientNarrationCoordinator>();
        services.AddSingleton<TurnResolver>();
        services.AddSingleton<GetScenarioHandler>();
        services.AddSingleton<ResolveTurnHandler>();
        services.AddSingleton<LockInHandler>();
        services.AddSingleton<ExploreDungeonHandler>();

        // Scores
        services.AddSingleton<GetHighScoresHandler>();

        services.AddSocial(configuration);
        return services;
    }
}
