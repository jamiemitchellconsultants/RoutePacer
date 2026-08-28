using Microsoft.AspNetCore.Components.WebAssembly.Server;

// Container healthcheck. The aspnet runtime image has no curl, so the app probes its own readiness
// endpoint and exits with the status Docker expects.
if (args.Contains("--healthcheck"))
{
    var port = (Environment.GetEnvironmentVariable("ASPNETCORE_HTTP_PORTS") ?? "8080").Split(';')[0];
    using var probe = new HttpClient { Timeout = TimeSpan.FromSeconds(4) };
    try { return (await probe.GetAsync($"http://127.0.0.1:{port}/health/ready")).IsSuccessStatusCode ? 0 : 1; }
    catch (Exception) { return 1; }
}

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddHealthChecks();

var app = builder.Build();
app.UseBlazorFrameworkFiles();
app.UseStaticFiles();
app.UseRouting();
// This server holds no state and reaches no dependency, so readiness and liveness answer the same
// question: is the process serving. They stay distinct endpoints because the container healthcheck
// and the deployment script both probe /health/ready by name.
app.MapHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions { Predicate = _ => false, ResponseWriter = static (context, _) => context.Response.WriteAsync("Healthy") });
app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions { Predicate = _ => false, ResponseWriter = static (context, _) => context.Response.WriteAsync("Healthy") });
app.MapFallback(async context =>
{
    if (context.Request.Path.StartsWithSegments("/api") || context.Request.Path.StartsWithSegments("/health")) { context.Response.StatusCode = StatusCodes.Status404NotFound; return; }
    var file = context.RequestServices.GetRequiredService<IWebHostEnvironment>().WebRootFileProvider.GetFileInfo("index.html");
    if (!file.Exists) { context.Response.StatusCode = StatusCodes.Status404NotFound; return; }
    context.Response.ContentType = "text/html";
    await using var stream = file.CreateReadStream();
    await stream.CopyToAsync(context.Response.Body, context.RequestAborted);
});

app.Run();
return 0;

public partial class Program { }
