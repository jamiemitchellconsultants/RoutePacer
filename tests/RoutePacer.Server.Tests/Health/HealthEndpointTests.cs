using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace RoutePacer.Server.Tests.Health;

// The server holds no state and reaches no dependency, so both probes answer the same question.
// They are still asserted separately: the container healthcheck and the deployment script probe
// /health/ready by name, and a rename would break both silently.
public sealed class HealthEndpointTests
{
    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    public async Task Both_probes_report_healthy(string path)
    {
        using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();

        var response = await client.GetAsync(path);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be("Healthy");
    }

    [Fact]
    public async Task An_unknown_api_path_is_not_served_the_application_shell()
    {
        using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();

        (await client.GetAsync("/api/anything")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
