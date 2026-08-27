using System.Net;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using RoutePacer.Server.Health;

namespace RoutePacer.Server.Tests.Health;

public sealed class HealthEndpointTests
{
    [Fact]
    public async Task Liveness_is_healthy_whenever_the_process_runs()
    {
        using var factory = new RelayApplicationFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/live");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be("Healthy");
    }

    [Fact]
    public async Task Liveness_is_anonymous_and_does_not_touch_the_database()
    {
        using var factory = new RelayApplicationFactory();
        using var client = factory.CreateClient();

        (await client.GetAsync("/health/live")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Readiness_is_unavailable_until_migrations_complete()
    {
        using var factory = new RelayApplicationFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/ready");

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task Migration_state_starts_incomplete()
    {
        using var factory = new RelayApplicationFactory();
        using var client = factory.CreateClient();
        _ = await client.GetAsync("/health/live");

        factory.Services.GetRequiredService<MigrationState>().IsComplete.Should().BeFalse();
    }

    [Theory]
    [InlineData("/api/does-not-exist")]
    [InlineData("/health/does-not-exist")]
    public async Task Misspelled_api_and_health_paths_do_not_receive_the_spa_shell(string path)
    {
        using var factory = new RelayApplicationFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync(path);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync()).Should().NotContain("<html");
    }
}
