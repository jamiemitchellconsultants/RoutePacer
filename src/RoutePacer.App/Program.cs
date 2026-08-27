using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using RoutePacer.App;
using RoutePacer.App.Browser;
using RoutePacer.App.Routes;
using RoutePacer.App.Rides;
using RoutePacer.App.Invocation;
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
builder.Services.AddScoped<InvocationParser>();
builder.Services.AddScoped<IInvocationSettingsProvider, ServerInvocationSettingsProvider>();
builder.Services.AddScoped<IInvocationVerifier, WebCryptoInvocationVerifier>();
builder.Services.AddScoped<HandoffPayloadClient>();
builder.Services.AddScoped<RouteTimerInvocationService>();

await builder.Build().RunAsync();
