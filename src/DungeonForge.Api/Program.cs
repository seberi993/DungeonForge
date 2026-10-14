using System.Text.Json;
using System.Text.Json.Serialization;
using DungeonForge.Api.Endpoints;
using DungeonForge.Api.Features.Parties.CastConfrontationVote;
using DungeonForge.Api.Features.Parties.Chaos;
using DungeonForge.Api.Features.Parties.ClaimVault;
using DungeonForge.Api.Features.Parties.CreateParty;
using DungeonForge.Api.Features.Parties.ExploreDungeon;
using DungeonForge.Api.Features.Parties.ExtractRun;
using DungeonForge.Api.Features.Parties.GetPartyState;
using DungeonForge.Api.Features.Parties.Realtime;
using DungeonForge.Api.Features.Scores;
using DungeonForge.Api.Features.Scores.GetHighScores;
using DungeonForge.Api.Infrastructure.Narration;
using DungeonForge.Api.Infrastructure.Scores;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

// Free-tier cap for single-instance host: every cache.Set must specify Size.
builder.Services.AddMemoryCache(o => o.SizeLimit = 1024);

// JSON contract is camelCase end-to-end (matches Blazor/System.Text.Json defaults).
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    o.SerializerOptions.DictionaryKeyPolicy = JsonNamingPolicy.CamelCase;
    o.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
});

// SignalR does not use the Minimal API JSON options; mirror them here.
builder.Services.AddSignalR().AddJsonProtocol(o =>
{
    o.PayloadSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    o.PayloadSerializerOptions.DictionaryKeyPolicy = JsonNamingPolicy.CamelCase;
    o.PayloadSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    o.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
});
builder.Services.TryAddSingleton<PlayerConnections>();
builder.Services.TryAddSingleton<IPartyNotifier, SignalRPartyNotifier>();

builder.Services.TryAddSingleton(TimeProvider.System);
builder.Services.AddSingleton<CreatePartyHandler>();
builder.Services.AddSingleton<GetPartyStateHandler>();
builder.Services.AddSingleton<DungeonForge.Api.Features.Parties.JoinByCode.JoinByCodeHandler>();
builder.Services.AddSingleton<DungeonForge.Api.Features.Parties.JoinParty.JoinPartyHandler>();
builder.Services.TryAddSingleton<IRoller, RandomRoller>();
// Chaos systems own their roller so their variance never disturbs the deterministic game-roll order.
builder.Services.TryAddSingleton(sp => new ChaosDispatcher(
    sp.GetRequiredService<IPartyNotifier>(), new RandomRoller(), sp.GetRequiredService<ILogger<ChaosDispatcher>>()));
builder.Services.TryAddSingleton(new VocabularySelector(new RandomRoller()));
builder.Services.AddSingleton<ExploreDungeonHandler>();
builder.Services.AddSingleton<ClaimVaultHandler>();
builder.Services.AddSingleton<CastConfrontationVoteHandler>();
builder.Services.AddSingleton<ExtractRunHandler>();
builder.Services.AddSingleton<DungeonForge.Api.Features.Parties.UnlockTurn.UnlockTurnHandler>();
builder.Services.AddOptions<NarrationOptions>()
    .Bind(builder.Configuration.GetSection("Narration"))
    .PostConfigure(o =>
    {
        o.ApiKeys = NarrationKeyResolver.Resolve(builder.Configuration);
        o.ApiKey = o.ApiKeys.FirstOrDefault();
    });
builder.Services.AddHttpClient<GeminiNarratorService>((sp, http) =>
    http.BaseAddress = new Uri(sp.GetRequiredService<IOptions<NarrationOptions>>().Value.BaseUrl));
builder.Services.TryAddSingleton<IKeyPool, KeyPool>();
builder.Services.TryAddSingleton<KeyPoolGeminiNarrator>();
builder.Services.TryAddSingleton<IDungeonNarrator>(sp =>
{
    var o = sp.GetRequiredService<IOptions<NarrationOptions>>().Value;
    return o.Enabled && o.ApiKeys.Length > 0
        ? sp.GetRequiredService<KeyPoolGeminiNarrator>()
        : new StaticDungeonNarrator();
});

var supabase = builder.Configuration.GetSection("Supabase").Get<SupabaseOptions>();
if (supabase is { IsConfigured: true })
{
    builder.Services.AddHttpClient<SupabaseScoreBoard>(http =>
    {
        http.BaseAddress = new Uri(supabase.Url!.TrimEnd('/') + "/");
        http.DefaultRequestHeaders.Add("apikey", supabase.ServiceKey);
        http.DefaultRequestHeaders.Add("Authorization", "Bearer " + supabase.ServiceKey);
    });
    builder.Services.TryAddSingleton<IScoreBoard>(sp => sp.GetRequiredService<SupabaseScoreBoard>());
}
else
{
    builder.Services.TryAddSingleton<IScoreBoard, InMemoryScoreBoard>();
}

builder.Services.AddSingleton<GetHighScoresHandler>();

const string CorsPolicy = "BlazorClient";
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(o => o.AddPolicy(CorsPolicy, p =>
    p.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod().AllowCredentials()));

builder.Services.AddEndpoints(typeof(Program).Assembly);

var app = builder.Build();

app.UseCors(CorsPolicy);

app.MapEndpoints();
app.MapHub<DungeonHub>("/hubs/dungeon").RequireCors(CorsPolicy);

app.Run();

public partial class Program;
