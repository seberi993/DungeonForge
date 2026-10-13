using System.Text.Json;
using System.Text.Json.Serialization;
using DungeonForge.Api.Endpoints;
using DungeonForge.Api.Features.Parties.CreateParty;
using DungeonForge.Api.Features.Parties.ExploreDungeon;
using DungeonForge.Api.Features.Parties.GetPartyState;
using Microsoft.Extensions.DependencyInjection.Extensions;

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

builder.Services.TryAddSingleton(TimeProvider.System);
builder.Services.AddSingleton<CreatePartyHandler>();
builder.Services.AddSingleton<GetPartyStateHandler>();
builder.Services.TryAddSingleton<IRoller, RandomRoller>();
builder.Services.AddSingleton<ExploreDungeonHandler>();

builder.Services.AddEndpoints(typeof(Program).Assembly);

var app = builder.Build();

app.MapEndpoints();

app.Run();

public partial class Program;
