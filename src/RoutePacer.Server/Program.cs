using Microsoft.AspNetCore.Components.WebAssembly.Server;
using Microsoft.EntityFrameworkCore;
using RoutePacer.Persistence;
using RoutePacer.Persistence.Handoffs;
using RoutePacer.Server.Configuration;
using RoutePacer.Server.Handoffs;
using RoutePacer.Server.Health;
using RoutePacer.Server.Hosting;
using System.Text.Json;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddOptions<HandoffRelayOptions>().Bind(builder.Configuration.GetSection("HandoffRelay"))
    .Validate(o => o.PublicOrigin == new Uri("https://pacetracking.tqaentry.com") && o.MaximumUploadBytes == 52_428_800 && o.Lifetime == TimeSpan.FromMinutes(10) && (!o.UploadsEnabled || !string.IsNullOrEmpty(o.UploadCredential)), "Relay settings do not match the fixed handoff contract.")
    .ValidateOnStart();
builder.Services.AddOptions<RouteTimerInvocationOptions>().Bind(builder.Configuration.GetSection("RouteTimerInvocation"))
    .Validate(o => !o.Enabled || IsPublicP256Jwk(o.PublicKeyJwk), "RouteTimer intake requires a public P-256 EC JWK.")
    .ValidateOnStart();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<MigrationState>();
var connectionString = builder.Configuration.GetConnectionString("RoutePacer") ?? "Host=localhost;Database=routepacer;Username=routepacer;Password=routepacer";
builder.Services.AddDbContextFactory<RoutePacerDbContext>(options => options.UseNpgsql(connectionString));
builder.Services.AddScoped<IHandoffStore, PostgresHandoffStore>();
builder.Services.AddScoped<HandoffUploadService>();
builder.Services.AddScoped<UploadCredentialVerifier>();
builder.Services.AddHostedService<HandoffCleanupService>();
builder.Services.AddHealthChecks().AddCheck<MigrationsReadyHealthCheck>("database-migrations", tags: ["ready"]);
builder.Services.AddHostedService<DatabaseMigrationService>();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    // Rate limiting is middleware, so it runs before the endpoint can authenticate. Partitioning on the
    // credential keeps anonymous traffic out of the relay's window, which one caller could otherwise
    // exhaust in ten requests and lock RouteTimer out.
    options.AddPolicy("handoff-upload", context => RateLimitPartition.GetFixedWindowLimiter(
        context.RequestServices.GetRequiredService<UploadCredentialVerifier>().IsValid(context.Request.Headers.Authorization.ToString()) ? "authenticated-uploads" : "anonymous-uploads",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, QueueProcessingOrder = QueueProcessingOrder.OldestFirst }));
});

var app = builder.Build();
app.UseBlazorFrameworkFiles();
app.UseStaticFiles();
app.UseRouting();
app.UseMiddleware<SensitiveRequestLoggingFilter>();
app.UseRateLimiter();
app.MapHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions { Predicate = _ => false, ResponseWriter = static (context, _) => context.Response.WriteAsync("Healthy") });
app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions { Predicate = check => check.Tags.Contains("ready"), ResponseWriter = static (context, _) => context.Response.WriteAsync("Healthy") });
app.MapHandoffEndpoints();
app.MapClientConfiguration();
app.MapFallback(async context =>
{
    if (context.Request.Path.StartsWithSegments("/api") || context.Request.Path.StartsWithSegments("/health")) { context.Response.StatusCode = StatusCodes.Status404NotFound; return; }
    var file = context.RequestServices.GetRequiredService<IWebHostEnvironment>().WebRootFileProvider.GetFileInfo("index.html");
    if (!file.Exists) { context.Response.StatusCode = StatusCodes.Status404NotFound; return; }
    context.Response.ContentType = "text/html";
    await using var stream = file.CreateReadStream();
    await stream.CopyToAsync(context.Response.Body, context.RequestAborted);
});
static bool IsPublicP256Jwk(string value)
{
    try
    {
        using var document = JsonDocument.Parse(value);
        var root = document.RootElement;
        return root.ValueKind == JsonValueKind.Object && root.TryGetProperty("kty", out var kty) && kty.GetString() == "EC" && root.TryGetProperty("crv", out var curve) && curve.GetString() == "P-256" && root.TryGetProperty("x", out _) && root.TryGetProperty("y", out _) && !root.TryGetProperty("d", out _);
    }
    catch (JsonException) { return false; }
}

app.Run();

public partial class Program { }
