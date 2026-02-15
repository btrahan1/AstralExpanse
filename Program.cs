using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using AstralExpanse;
using AstralExpanse.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
builder.Services.AddScoped<GameStateService>();
builder.Services.AddScoped<BabylonService>();
builder.Services.AddScoped<PersistenceService>();
builder.Services.AddScoped<MissionService>();
builder.Services.AddScoped<ConstructionService>();

await builder.Build().RunAsync();
