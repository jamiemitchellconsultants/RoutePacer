using System.Net;
using System.Text.Json;
using FluentAssertions;

namespace RoutePacer.Server.Tests.Configuration;

public sealed class ClientConfigurationEndpointTests
{
    private const string PublicJwk = """{"kty":"EC","crv":"P-256","x":"UtdWHp_xeGuOkarqYW_IGdtg5osMQWJNFEhxyyE5eXs","y":"GK7gB55K5rtaXVbsm7mKWE4kLH_8V-2TKxYzq5SaVNI"}""";

    [Fact]
    public async Task While_disabled_only_the_flag_is_published()
    {
        using var factory = new RelayApplicationFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/config/route-timer-invocation");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        document.RootElement.GetProperty("enabled").GetBoolean().Should().BeFalse();
        document.RootElement.TryGetProperty("publicKeyJwk", out _).Should().BeFalse();
        response.Headers.CacheControl!.ToString().Should().Be("no-store");
    }

    [Fact]
    public async Task While_enabled_the_public_jwk_is_published_as_an_object()
    {
        using var factory = new RelayApplicationFactory { IntakeEnabled = true, PublicKeyJwk = PublicJwk };
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/config/route-timer-invocation");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        document.RootElement.GetProperty("enabled").GetBoolean().Should().BeTrue();
        var key = document.RootElement.GetProperty("publicKeyJwk");
        key.ValueKind.Should().Be(JsonValueKind.Object);
        key.GetProperty("crv").GetString().Should().Be("P-256");
        key.TryGetProperty("d", out _).Should().BeFalse();
        response.Headers.CacheControl!.ToString().Should().Be("no-store");
    }

    [Fact]
    public async Task The_endpoint_is_anonymous()
    {
        using var factory = new RelayApplicationFactory();
        using var client = factory.CreateClient();

        (await client.GetAsync("/api/config/route-timer-invocation")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("""{"kty":"RSA","n":"x","e":"AQAB"}""")]
    [InlineData("""{"kty":"EC","crv":"P-384","x":"a","y":"b"}""")]
    [InlineData("""{"kty":"EC","crv":"P-256","x":"a","y":"b","d":"private"}""")]
    public void Enabling_intake_with_an_unacceptable_key_fails_startup(string jwk)
    {
        using var factory = new RelayApplicationFactory { IntakeEnabled = true, PublicKeyJwk = jwk };

        var act = () => factory.CreateClient();

        act.Should().Throw<Exception>();
    }
}
