using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using RoutePacer.App;
using RoutePacer.App.Browser;
using RoutePacer.App.Routes;
using RoutePacer.App.Rides;
using RoutePacer.App.Storage;
using RoutePacer.Core.Import;
using RoutePacer.Core.Storage;
using RoutePacer.Core.Tracking;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<IIndexedDbModule, IndexedDbModule>();
builder.Services.AddScoped<IRouteRepository, IndexedDbRouteRepository>();
builder.Services.AddScoped<IRideRepository, IndexedDbRideRepository>();
builder.Services.AddSingleton<RouteNormalizer>();
builder.Services.AddSingleton<IRouteFileParser, GpxRouteParser>();
builder.Services.AddSingleton<IRouteFileParser, FitRouteParser>();
builder.Services.AddSingleton(sp => new RouteImportService(sp.GetServices<IRouteFileParser>().ToArray(), sp.GetRequiredService<RouteNormalizer>()));
builder.Services.AddScoped<RouteCatalogService>();
builder.Services.AddSingleton<RouteMatcher>();
builder.Services.AddSingleton<PacingService>();
builder.Services.AddScoped<ILocationService, LocationService>();
builder.Services.AddScoped<IWakeLockService, WakeLockService>();
builder.Services.AddScoped<RideSessionService>();

var host = builder.Build();

// A ride left in progress by a crash, reload, or evicted tab is restored -- paused -- before the UI
// renders. Recovery never resumes GPS and never requests location permission.
await using (var scope = host.Services.CreateAsyncScope())
{
    try { await scope.ServiceProvider.GetRequiredService<RideSessionService>().RestoreActiveRideAsync(); }
    catch (Exception) { /* Storage is unavailable; recovery is retried on the next start. */ }
}

await host.RunAsync();
