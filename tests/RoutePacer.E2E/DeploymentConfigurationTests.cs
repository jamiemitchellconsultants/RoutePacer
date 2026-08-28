using System.Diagnostics;
using System.Text.Json;
using FluentAssertions;

namespace RoutePacer.E2E;

public sealed class DeploymentConfigurationTests
{
    private static JsonDocument Config(string file)
    {
        var info = new ProcessStartInfo("docker", $"compose -f {file} config --format json")
        {
            WorkingDirectory = RepositoryRoot.Path,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        using var process = Process.Start(info) ?? throw new InvalidOperationException("docker could not be started.");
        var json = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        process.ExitCode.Should().Be(0, "docker compose config must succeed: {0}", error);
        return JsonDocument.Parse(json);
    }

    private static JsonDocument Production() => Config("deploy/docker-compose.yml");
    private static JsonElement Service(JsonDocument config, string name) => config.RootElement.GetProperty("services").GetProperty(name);

    [Fact]
    public void The_deployment_is_one_stateless_container_behind_the_shared_ingress()
    {
        using var config = Production();

        config.RootElement.GetProperty("services").EnumerateObject().Select(s => s.Name).Should().BeEquivalentTo(["routepacer"]);
        Service(config, "routepacer").TryGetProperty("ports", out _).Should().BeFalse();
        Service(config, "routepacer").GetProperty("networks").EnumerateObject().Select(n => n.Name).Should().BeEquivalentTo(["mcp-public"]);
    }

    // The relay is gone, so the deployment must stay free of the machinery it needed. A database or a
    // secret reappearing here is the signal that server-side state has crept back in, which is the thing
    // privacy.md now promises is absent.
    [Fact]
    public void The_deployment_declares_no_volume_and_no_secret()
    {
        using var config = Production();

        config.RootElement.TryGetProperty("volumes", out _).Should().BeFalse();
        config.RootElement.TryGetProperty("secrets", out _).Should().BeFalse();

        var environment = Service(config, "routepacer").GetProperty("environment").EnumerateObject().Select(e => e.Name).ToArray();
        environment.Should().BeEquivalentTo(["ASPNETCORE_HTTP_PORTS"]);
    }

    [Fact]
    public void The_container_reports_its_own_readiness_and_restarts_unless_stopped()
    {
        using var config = Production();

        Service(config, "routepacer").GetProperty("healthcheck").GetProperty("test").ToString().Should().Contain("healthcheck");
        Service(config, "routepacer").GetProperty("restart").GetString().Should().Be("unless-stopped");
    }

    [Fact]
    public void The_local_stack_publishes_only_on_the_loopback_interface()
    {
        using var config = Config("deploy/docker-compose.local.yml");

        var published = Service(config, "routepacer").GetProperty("ports").EnumerateArray().Single();
        published.GetProperty("host_ip").GetString().Should().Be("127.0.0.1");
    }

    [Fact]
    public void The_caddy_fragment_discards_access_logs_for_the_production_origin()
    {
        var caddy = File.ReadAllText(RepositoryRoot.Combine("deploy", "caddy", "routepacer.caddy"));

        caddy.Should().Contain("pacetracking.tqaentry.com");
        caddy.Should().MatchRegex(@"log\s*\{\s*output\s+discard\s*\}");
        caddy.Should().Contain("reverse_proxy routepacer:8080");
    }

    [Fact]
    public void No_public_asset_carries_key_or_credential_material()
    {
        var wwwroot = RepositoryRoot.Combine("src", "RoutePacer.App", "wwwroot");
        var offenders = Directory.EnumerateFiles(wwwroot, "*", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}lib{Path.DirectorySeparatorChar}"))
            .Where(f => File.ReadAllText(f).Contains("PRIVATE KEY", StringComparison.OrdinalIgnoreCase)
                     || File.ReadAllText(f).Contains("Credential", StringComparison.OrdinalIgnoreCase)
                     || File.ReadAllText(f).Contains("HMACSHA", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        offenders.Should().BeEmpty();
    }

    // appsettings.json is published inside the image, so a setting added here reaches every deployment.
    [Fact]
    public void Tracked_server_settings_configure_nothing_but_logging()
    {
        using var settings = JsonDocument.Parse(File.ReadAllText(RepositoryRoot.Combine("src", "RoutePacer.Server", "appsettings.json")));

        settings.RootElement.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(["Logging", "AllowedHosts"]);
    }
}
