using System.Diagnostics;
using System.Net.Sockets;

namespace RoutePacer.E2E;

/// <summary>
/// Publishes RoutePacer.Server and runs it under Kestrel so a real browser loads the real published PWA:
/// the Blazor assets, service worker, and manifest, not a test double. The relay database is not needed,
/// so migrations stay off and readiness is not used as the startup signal.
/// </summary>
public sealed class PublishedAppFixture : IAsyncLifetime
{
    private Process? server;

    public string BaseUrl { get; private set; } = "";

    public async Task InitializeAsync()
    {
        var output = Path.Combine(Path.GetTempPath(), "routepacer-e2e", Guid.NewGuid().ToString("N"));
        Run("dotnet", $"publish src/RoutePacer.Server/RoutePacer.Server.csproj -c Release -o \"{output}\"", RepositoryRoot.Path);

        var port = FreePort();
        BaseUrl = $"http://127.0.0.1:{port}";

        var info = new ProcessStartInfo("dotnet", $"\"{Path.Combine(output, "RoutePacer.Server.dll")}\"")
        {
            WorkingDirectory = output,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        info.Environment["ASPNETCORE_URLS"] = BaseUrl;
        info.Environment["ASPNETCORE_ENVIRONMENT"] = "Production";
        info.Environment["Database__ApplyMigrations"] = "false";
        info.Environment["HandoffRelay__UploadsEnabled"] = "false";
        info.Environment["RouteTimerInvocation__Enabled"] = "false";

        server = Process.Start(info) ?? throw new InvalidOperationException("The published server could not be started.");

        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        for (var attempt = 0; attempt < 60; attempt++)
        {
            try
            {
                if ((await client.GetAsync($"{BaseUrl}/health/live")).IsSuccessStatusCode) return;
            }
            catch (Exception) { /* not listening yet */ }
            await Task.Delay(500);
        }
        throw new InvalidOperationException("The published server did not become live.");
    }

    public Task DisposeAsync()
    {
        if (server is { HasExited: false }) server.Kill(entireProcessTree: true);
        server?.Dispose();
        return Task.CompletedTask;
    }

    private static int FreePort()
    {
        using var listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static void Run(string file, string arguments, string workingDirectory)
    {
        using var process = Process.Start(new ProcessStartInfo(file, arguments)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        })!;
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0) throw new InvalidOperationException($"{file} {arguments} failed:\n{output}\n{error}");
    }
}

[CollectionDefinition(nameof(PublishedAppCollection))]
public sealed class PublishedAppCollection : ICollectionFixture<PublishedAppFixture>;
