using System.Net;
using FluentAssertions;
using RoutePacer.Persistence.Handoffs;

namespace RoutePacer.Server.Tests.Handoffs;

public sealed class HandoffConsumptionTests
{
    private const string Token = "9Xq2mWbTf4LpN0sRvEjKcYzA7dHuG1iObQ3xVn5MtSw";

    [Fact]
    public async Task First_get_returns_exact_bytes_and_required_headers_then_second_get_is_404()
    {
        using var factory = new RelayApplicationFactory();
        factory.Store.Seed(Token, "<gpx>exact</gpx>"u8.ToArray(), factory.Clock.GetUtcNow().AddMinutes(10));
        using var client = factory.CreateClient();

        var first = await client.GetAsync($"/api/handoffs/{Token}");
        var second = await client.GetAsync($"/api/handoffs/{Token}");

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        (await first.Content.ReadAsStringAsync()).Should().Be("<gpx>exact</gpx>");
        first.Content.Headers.ContentType!.MediaType.Should().Be("application/gpx+xml");
        first.Content.Headers.ContentLength.Should().Be((await first.Content.ReadAsByteArrayAsync()).Length);
        first.Headers.CacheControl!.ToString().Should().Be("no-store");
        first.Headers.Pragma.ToString().Should().Contain("no-cache");
        first.Headers.GetValues("X-Content-Type-Options").Single().Should().Be("nosniff");

        second.StatusCode.Should().Be(HttpStatusCode.NotFound);
        factory.Store.Rows.Should().BeEmpty();
    }

    [Fact]
    public async Task Every_failure_mode_produces_an_indistinguishable_response()
    {
        using var factory = new RelayApplicationFactory();
        factory.Store.Seed(Token, "<gpx/>"u8.ToArray(), factory.Clock.GetUtcNow().AddMinutes(10));
        using var client = factory.CreateClient();
        await client.GetAsync($"/api/handoffs/{Token}");                         // consume it

        var consumed = await client.GetAsync($"/api/handoffs/{Token}");
        var unknown = await client.GetAsync("/api/handoffs/AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA");
        var malformed = await client.GetAsync("/api/handoffs/too-short");
        var padded = await client.GetAsync("/api/handoffs/9Xq2mWbTf4LpN0sRvEjKcYzA7dHuG1iObQ3xVn5MtS=");

        foreach (var response in new[] { consumed, unknown, malformed, padded })
        {
            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
            response.Content.Headers.ContentLength.Should().Be(0);
            (await response.Content.ReadAsStringAsync()).Should().BeEmpty();
            response.Headers.CacheControl!.ToString().Should().Be("no-store");
            response.Headers.GetValues("X-Content-Type-Options").Single().Should().Be("nosniff");
        }
    }

    [Fact]
    public async Task An_expired_row_is_not_returned()
    {
        using var factory = new RelayApplicationFactory();
        factory.Store.Seed(Token, "<gpx/>"u8.ToArray(), factory.Clock.GetUtcNow().AddMinutes(10));
        using var client = factory.CreateClient();
        factory.Clock.Advance(TimeSpan.FromMinutes(10));

        (await client.GetAsync($"/api/handoffs/{Token}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_row_one_second_inside_its_lifetime_is_still_returned()
    {
        using var factory = new RelayApplicationFactory();
        factory.Store.Seed(Token, "<gpx/>"u8.ToArray(), factory.Clock.GetUtcNow().AddMinutes(10));
        using var client = factory.CreateClient();
        factory.Clock.Advance(TimeSpan.FromMinutes(10) - TimeSpan.FromSeconds(1));

        (await client.GetAsync($"/api/handoffs/{Token}")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("too-short")]
    [InlineData("9Xq2mWbTf4LpN0sRvEjKcYzA7dHuG1iObQ3xVn5MtSww")]
    [InlineData("9Xq2mWbTf4LpN0sRvEjKcYzA7dHuG1iObQ3xVn5Mt$w")]
    public async Task A_token_failing_the_shape_check_never_reaches_the_store(string token)
    {
        using var factory = new RelayApplicationFactory();
        using var client = factory.CreateClient();

        await client.GetAsync($"/api/handoffs/{token}");

        factory.Store.ConsumeCount.Should().Be(0);
    }

    [Fact]
    public async Task Consumption_is_anonymous()
    {
        using var factory = new RelayApplicationFactory();
        factory.Store.Seed(Token, "<gpx/>"u8.ToArray(), factory.Clock.GetUtcNow().AddMinutes(10));
        using var client = factory.CreateClient();

        // No Authorization header at all.
        (await client.GetAsync($"/api/handoffs/{Token}")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task The_hash_of_the_decoded_token_addresses_the_row()
    {
        using var factory = new RelayApplicationFactory();
        var expected = HandoffToken.Hash(Token);
        factory.Store.Seed(Token, "<gpx/>"u8.ToArray(), factory.Clock.GetUtcNow().AddMinutes(10));

        factory.Store.Rows.Keys.Single().Should().Be(Convert.ToHexString(expected));
    }
}
