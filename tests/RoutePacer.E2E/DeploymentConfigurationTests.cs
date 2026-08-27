using System.Diagnostics;
using System.Text.Json;
using FluentAssertions;

namespace RoutePacer.E2E;

public sealed class DeploymentConfigurationTests
{
    private static JsonDocument Config(string file, params (string Key, string Value)[] environment)
    {
        var info = new ProcessStartInfo("docker", $"compose -f {file} config --format json")
        {
            WorkingDirectory = RepositoryRoot.Path,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var (key, value) in environment) info.Environment[key] = value;

        using var process = Process.Start(info) ?? throw new InvalidOperationException("docker could not be started.");
        var json = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        process.ExitCode.Should().Be(0, "docker compose config must succeed: {0}", error);
        return JsonDocument.Parse(json);
    }

    private static JsonDocument Production() => Config("deploy/docker-compose.yml",
        ("ROUTEPACER_DB_PASSWORD", "test"), ("ROUTEPACER_RELAY_UPLOAD_KEY", "test-only"), ("ROUTEPACER_ROUTE_TIMER_PUBLIC_JWK", "{}"));

    private static JsonElement Service(JsonDocument config, string name) => config.RootElement.GetProperty("services").GetProperty(name);
    private static string[] ServiceNetworks(JsonDocument config, string name)
        => [.. Service(config, name).GetProperty("networks").EnumerateObject().Select(n => n.Name)];

    [Fact]
    public void Neither_service_publishes_a_host_port()
    {
        using var config = Production();

        Service(config, "routepacer-db").TryGetProperty("ports", out _).Should().BeFalse();
        Service(config, "routepacer").TryGetProperty("ports", out _).Should().BeFalse();
    }

    [Fact]
    public void The_database_is_reachable_only_on_the_internal_network()
    {
        using var config = Production();

        config.RootElement.GetProperty("networks").GetProperty("routepacer-private").GetProperty("internal").GetBoolean().Should().BeTrue();
        ServiceNetworks(config, "routepacer-db").Should().Contain("routepacer-private").And.NotContain("mcp-public");
        ServiceNetworks(config, "routepacer").Should().Contain("mcp-public");
    }

    [Fact]
    public void Exactly_one_named_volume_holds_the_database_and_there_is_no_backup_service()
    {
        using var config = Production();

        config.RootElement.GetProperty("volumes").EnumerateObject().Select(v => v.Name).Should().ContainSingle().Which.Should().Contain("routepacer_postgres");
        config.RootElement.GetProperty("services").EnumerateObject().Select(s => s.Name)
            .Should().BeEquivalentTo(["routepacer", "routepacer-db"]);
    }

    [Fact]
    public void The_app_waits_for_a_healthy_database_and_reports_its_own_readiness()
    {
        using var config = Production();

        Service(config, "routepacer-db").GetProperty("healthcheck").Should().NotBeNull();
        Service(config, "routepacer").GetProperty("depends_on").GetProperty("routepacer-db")
            .GetProperty("condition").GetString().Should().Be("service_healthy");
        Service(config, "routepacer").GetProperty("healthcheck").GetProperty("test").ToString().Should().Contain("healthcheck");
        Service(config, "routepacer").GetProperty("restart").GetString().Should().Be("unless-stopped");
    }

    [Fact]
    public void Both_handoff_controls_default_to_disabled()
    {
        using var config = Production();
        var environment = Service(config, "routepacer").GetProperty("environment");

        environment.GetProperty("HandoffRelay__UploadsEnabled").GetString().Should().Be("false");
        environment.GetProperty("RouteTimerInvocation__Enabled").GetString().Should().Be("false");
        environment.GetProperty("HandoffRelay__PublicOrigin").GetString().Should().Be("https://pacetracking.tqaentry.com");
        environment.GetProperty("Database__ApplyMigrations").GetString().Should().Be("true");
    }

    [Fact]
    public void The_local_stack_publishes_only_on_the_loopback_interface_with_both_controls_off()
    {
        using var config = Config("deploy/docker-compose.local.yml");
        var app = Service(config, "routepacer");

        var published = app.GetProperty("ports").EnumerateArray().Single();
        published.GetProperty("host_ip").GetString().Should().Be("127.0.0.1");
        Service(config, "routepacer-db").TryGetProperty("ports", out _).Should().BeFalse();
        app.GetProperty("environment").GetProperty("HandoffRelay__UploadsEnabled").GetString().Should().Be("false");
        app.GetProperty("environment").GetProperty("RouteTimerInvocation__Enabled").GetString().Should().Be("false");
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
    public void No_public_asset_carries_a_key_token_or_upload_credential()
    {
        var wwwroot = RepositoryRoot.Combine("src", "RoutePacer.App", "wwwroot");
        var offenders = Directory.EnumerateFiles(wwwroot, "*", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}lib{Path.DirectorySeparatorChar}"))
            .Where(f => File.ReadAllText(f).Contains("PRIVATE KEY", StringComparison.OrdinalIgnoreCase)
                     || File.ReadAllText(f).Contains("UploadCredential", StringComparison.OrdinalIgnoreCase)
                     || File.ReadAllText(f).Contains("RelayUpload", StringComparison.OrdinalIgnoreCase)
                     || File.ReadAllText(f).Contains("HMACSHA", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        offenders.Should().BeEmpty();
    }

    [Fact]
    public void Tracked_server_settings_keep_both_handoff_features_disabled()
    {
        using var settings = JsonDocument.Parse(File.ReadAllText(RepositoryRoot.Combine("src", "RoutePacer.Server", "appsettings.json")));

        settings.RootElement.GetProperty("HandoffRelay").GetProperty("UploadsEnabled").GetBoolean().Should().BeFalse();
        settings.RootElement.GetProperty("RouteTimerInvocation").GetProperty("Enabled").GetBoolean().Should().BeFalse();
        settings.RootElement.GetProperty("RouteTimerInvocation").GetProperty("PublicKeyJwk").GetString().Should().BeEmpty();
    }
}
