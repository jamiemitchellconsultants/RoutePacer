using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using RoutePacer.Server.Handoffs;

namespace RoutePacer.Server.Tests.Handoffs;

public sealed class HandoffCreationTests
{
    private static HttpRequestMessage Upload(string body, string? credential = RelayApplicationFactory.UploadCredential, string mediaType = "application/gpx+xml")
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/handoffs") { Content = new StringContent(body) };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue(mediaType);
        if (credential is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential);
        return request;
    }

    [Fact]
    public async Task Valid_upload_returns_exact_origin_and_ten_minute_expiry()
    {
        using var factory = new RelayApplicationFactory();
        using var client = factory.CreateClient();

        var response = await client.SendAsync(Upload("<gpx/>"));
        var body = await response.Content.ReadFromJsonAsync<HandoffCreatedResponse>();

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        body!.PayloadUrl.Should().MatchRegex(@"^https://pacetracking\.tqaentry\.com/api/handoffs/[A-Za-z0-9_-]{43}$");
        body.ExpiresAt.Should().Be(factory.Clock.GetUtcNow().AddMinutes(10));
        response.Headers.CacheControl!.ToString().Should().Be("no-store");
    }

    [Fact]
    public async Task The_stored_content_is_the_exact_request_bytes()
    {
        using var factory = new RelayApplicationFactory();
        using var client = factory.CreateClient();

        await client.SendAsync(Upload("<gpx>exact bytes</gpx>"));

        factory.Store.Rows.Should().ContainSingle()
            .Which.Value.Content.Should().Equal("<gpx>exact bytes</gpx>"u8.ToArray());
    }

    [Fact]
    public async Task The_plaintext_token_is_never_stored()
    {
        using var factory = new RelayApplicationFactory();
        using var client = factory.CreateClient();

        var body = await (await client.SendAsync(Upload("<gpx/>"))).Content.ReadFromJsonAsync<HandoffCreatedResponse>();
        var token = body!.PayloadUrl.Split('/')[^1];

        factory.Store.Rows.Keys.Should().NotContain(token);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("wrong-credential")]
    public async Task A_missing_or_wrong_credential_is_unauthorized(string? credential)
    {
        using var factory = new RelayApplicationFactory();
        using var client = factory.CreateClient();

        var response = await client.SendAsync(Upload("<gpx/>", credential));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        factory.Store.InsertCount.Should().Be(0);
    }

    [Fact]
    public async Task An_empty_body_is_a_bad_request()
    {
        using var factory = new RelayApplicationFactory();
        using var client = factory.CreateClient();

        (await client.SendAsync(Upload(""))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("application/xml")]
    [InlineData("text/xml")]
    [InlineData("application/octet-stream")]
    public async Task Any_other_media_type_is_rejected(string mediaType)
    {
        using var factory = new RelayApplicationFactory();
        using var client = factory.CreateClient();

        var response = await client.SendAsync(Upload("<gpx/>", mediaType: mediaType));

        response.StatusCode.Should().Be(HttpStatusCode.UnsupportedMediaType);
        factory.Store.InsertCount.Should().Be(0);
    }

    [Fact]
    public async Task A_declared_length_above_the_maximum_is_rejected_before_reading()
    {
        using var factory = new RelayApplicationFactory();
        using var client = factory.CreateClient();
        var request = Upload("<gpx/>");
        request.Content!.Headers.ContentLength = 52_428_801;

        var response = await client.SendAsync(request);

        ((int)response.StatusCode).Should().Be(413);
    }

    [Fact]
    public async Task A_store_failure_is_reported_without_detail()
    {
        using var factory = new RelayApplicationFactory();
        factory.Store.InsertFailure = new InvalidOperationException("connection string secret");
        using var client = factory.CreateClient();

        var response = await client.SendAsync(Upload("<gpx/>"));

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        (await response.Content.ReadAsStringAsync()).Should().NotContain("secret");
    }

    [Fact]
    public async Task Uploads_are_unavailable_while_the_feature_is_disabled()
    {
        using var factory = new RelayApplicationFactory { UploadsEnabled = false };
        using var client = factory.CreateClient();

        var response = await client.SendAsync(Upload("<gpx/>"));

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        factory.Store.InsertCount.Should().Be(0);
    }

    [Fact]
    public async Task Exhausting_the_window_returns_too_many_requests_not_service_unavailable()
    {
        using var factory = new RelayApplicationFactory();
        using var client = factory.CreateClient();

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 12; i++) statuses.Add((await client.SendAsync(Upload("<gpx/>"))).StatusCode);

        statuses.Should().Contain(HttpStatusCode.TooManyRequests);
        statuses.Should().NotContain(HttpStatusCode.ServiceUnavailable);
        statuses.Count(s => s == HttpStatusCode.Created).Should().Be(10);
    }

    [Fact]
    public async Task Anonymous_traffic_cannot_exhaust_the_authenticated_window()
    {
        using var factory = new RelayApplicationFactory();
        using var client = factory.CreateClient();

        for (var i = 0; i < 12; i++) await client.SendAsync(Upload("<gpx/>", credential: "wrong"));

        var response = await client.SendAsync(Upload("<gpx/>"));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }
}
