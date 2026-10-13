using DungeonForge.Web;
using DungeonForge.Web.Services;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

var apiBaseUrl = builder.Configuration["ApiBaseUrl"] ?? "http://localhost:5062";
builder.Services.AddHttpClient<DungeonApiClient>(client => client.BaseAddress = new Uri(apiBaseUrl));
builder.Services.AddScoped<GameSession>();
builder.Services.AddScoped<IDungeonHubClient>(_ => new DungeonHubClient(apiBaseUrl.TrimEnd('/') + "/hubs/dungeon"));

await builder.Build().RunAsync();
