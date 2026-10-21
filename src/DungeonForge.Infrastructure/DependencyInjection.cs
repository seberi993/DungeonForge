using DungeonForge.Application.Features.Parties;
using DungeonForge.Application.Features.Scores;
using DungeonForge.Application.Narration;
using DungeonForge.Infrastructure.Narration;
using DungeonForge.Infrastructure.Parties;
using DungeonForge.Infrastructure.Scores;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace DungeonForge.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // Free-tier cap for a single-instance host: every cache.Set must specify Size.
        services.AddMemoryCache(o => o.SizeLimit = 1024);
        services.TryAddSingleton<IPartyStore, PartyStore>();

        services.AddNarration(configuration);
        services.AddScoreBoard(configuration);
        return services;
    }

    private static void AddNarration(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<NarrationOptions>()
            .Bind(configuration.GetSection("Narration"))
            .PostConfigure(o =>
            {
                o.ApiKeys = NarrationKeyResolver.Resolve(configuration);
                o.ApiKey = o.ApiKeys.FirstOrDefault();
            });
        services.AddHttpClient<GeminiNarratorService>((sp, http) =>
            http.BaseAddress = new Uri(sp.GetRequiredService<IOptions<NarrationOptions>>().Value.BaseUrl));
        services.TryAddSingleton<IKeyPool, KeyPool>();
        services.TryAddSingleton<KeyPoolGeminiNarrator>();
        services.TryAddSingleton<IDungeonNarrator>(sp =>
        {
            var o = sp.GetRequiredService<IOptions<NarrationOptions>>().Value;
            return o.Enabled && o.ApiKeys.Length > 0
                ? sp.GetRequiredService<KeyPoolGeminiNarrator>()
                : new StaticDungeonNarrator();
        });

        // The social game's model-backed parsers share the same key pool; they fall back to keyword rules on any failure.
        services.AddHttpClient<GeminiJsonModel>((sp, http) =>
            http.BaseAddress = new Uri(sp.GetRequiredService<IOptions<NarrationOptions>>().Value.BaseUrl));
        services.TryAddSingleton<IJsonModel>(sp => sp.GetRequiredService<GeminiJsonModel>());
    }

    private static void AddScoreBoard(this IServiceCollection services, IConfiguration configuration)
    {
        var supabase = configuration.GetSection("Supabase").Get<SupabaseOptions>();
        if (supabase is { IsConfigured: true })
        {
            services.AddHttpClient<SupabaseScoreBoard>(http =>
            {
                http.BaseAddress = new Uri(supabase.Url!.TrimEnd('/') + "/");
                http.DefaultRequestHeaders.Add("apikey", supabase.ServiceKey);
                http.DefaultRequestHeaders.Add("Authorization", "Bearer " + supabase.ServiceKey);
            });
            services.TryAddSingleton<IScoreBoard>(sp => sp.GetRequiredService<SupabaseScoreBoard>());
        }
        else
        {
            services.TryAddSingleton<IScoreBoard, InMemoryScoreBoard>();
        }
    }
}
