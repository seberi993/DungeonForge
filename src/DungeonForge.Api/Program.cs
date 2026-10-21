using System.Text.Json;
using System.Text.Json.Serialization;
using DungeonForge.Api.Endpoints;
using DungeonForge.Api.Http;
using DungeonForge.Api.Realtime;
using DungeonForge.Application;
using DungeonForge.Application.Features.Parties.Realtime;
using DungeonForge.Infrastructure;
using Microsoft.Extensions.DependencyInjection.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplication(builder.Configuration);
builder.Services.AddInfrastructure(builder.Configuration);

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

const string CorsPolicy = "BlazorClient";
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(o => o.AddPolicy(CorsPolicy, p =>
    p.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod().AllowCredentials()));

builder.Services.AddEndpoints(typeof(Program).Assembly);

// Unhandled and unbindable-request failures become ProblemDetails. Throwing on a bad request in every environment (not
// only Development) is what routes it to the handler instead of a bare 400.
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<PipelineExceptionHandler>();
builder.Services.Configure<RouteHandlerOptions>(o => o.ThrowOnBadRequest = true);

var app = builder.Build();

app.UseCors(CorsPolicy);
app.UseExceptionHandler();

app.MapEndpoints();
app.MapHub<DungeonHub>("/hubs/dungeon").RequireCors(CorsPolicy);

app.Run();

public partial class Program;
