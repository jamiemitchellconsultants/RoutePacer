using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using RoutePacer.App.Invocation;
using RoutePacer.App.Routes;
using RoutePacer.Core.Import;

namespace RoutePacer.App.Tests.Invocation;

public sealed class RouteTimerInvocationServiceTests
{
    private const string Gpx = """
        <gpx version="1.1" xmlns="http://www.topografix.com/GPX/1/1"><trk><trkseg>
        <trkpt lat="0.0" lon="0.000"><time>2026-08-27T12:00:00Z</time></trkpt>
        <trkpt lat="0.0" lon="0.001"><time>2026-08-27T12:00:10Z</time></trkpt>
        <trkpt lat="0.0" lon="0.002"><time>2026-08-27T12:00:20Z</time></trkpt>
        </trkseg></trk></gpx>
        """;

    private readonly ContractFixture fixture = ContractFixture.Load();
    private readonly InMemoryRouteRepository routes = new();
    private readonly InMemoryRideRepository rides = new();
    private readonly StubSettings settings = new();
    private readonly StubVerifier verifier = new();
    private readonly FakeTimeProvider clock;

    public RouteTimerInvocationServiceTests() => clock = new FakeTimeProvider(ContractFixture.Load().IssuedAt.AddMinutes(1));

    private sealed class StubSettings : IInvocationSettingsProvider
    {
        public bool Enabled { get; set; } = true;
        public string? Jwk { get; set; } = "{\"kty\":\"EC\"}";
        public Exception? Failure { get; set; }
        public Task<(bool Enabled, string? PublicKeyJwk)> GetAsync(CancellationToken cancellationToken = default)
            => Failure is not null ? throw Failure : Task.FromResult((Enabled, Jwk));
    }

    private sealed class StubVerifier : IInvocationVerifier
    {
        public bool Result { get; set; } = true;
        public Exception? Failure { get; set; }
        public Task<bool> VerifyAsync(InvocationRequest request, byte[] canonicalBytes, string publicJwk, CancellationToken cancellationToken = default)
            => Failure is not null ? throw Failure : Task.FromResult(Result);
    }

    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public int Requests { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            var response = new HttpResponseMessage(status) { Content = new StringContent(body) };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/gpx+xml");
            return Task.FromResult(response);
        }
    }

    private (RouteTimerInvocationService Service, StubHandler Handler) Create(HttpStatusCode status = HttpStatusCode.OK, string body = Gpx)
    {
        var handler = new StubHandler(status, body);
        var catalog = new RouteCatalogService(new RouteImportService([new GpxRouteParser()], new RouteNormalizer()), routes, rides, clock);
        return (new RouteTimerInvocationService(new InvocationParser(), settings, verifier, new HandoffPayloadClient(new HttpClient(handler)), catalog, clock), handler);
    }

    private Uri Invocation => new(fixture.InvocationUrl);

    [Fact]
    public async Task A_verified_link_imports_through_the_same_pipeline_as_a_manual_gpx()
    {
        var (service, handler) = Create();

        var summary = await service.ImportAsync(Invocation);

        summary.Name.Should().Be(fixture.Name);
        handler.Requests.Should().Be(1);
        (await routes.ListAsync()).Should().ContainSingle().Which.RouteId.Should().Be(summary.RouteId);
        (await routes.GetAsync(summary.RouteId))!.HasTiming.Should().BeTrue();
    }

    [Fact]
    public async Task Verification_happens_before_the_payload_is_fetched()
    {
        verifier.Result = false;
        var (service, handler) = Create();

        var failure = (await service.Invoking(s => s.ImportAsync(Invocation)).Should().ThrowAsync<InvocationFailedException>()).Subject.Single();

        handler.Requests.Should().Be(0);
        failure.Retryable.Should().BeFalse();
        (await routes.ListAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task An_invalid_link_is_retryable_and_never_dispatches_a_request()
    {
        var (service, handler) = Create();

        var failure = (await service.Invoking(s => s.ImportAsync(new Uri("https://pacetracking.tqaentry.com/open?src=Nope")))
            .Should().ThrowAsync<InvocationFailedException>()).Subject.Single();

        failure.Retryable.Should().BeTrue();
        handler.Requests.Should().Be(0);
    }

    [Fact]
    public async Task A_configuration_failure_is_retryable()
    {
        settings.Failure = new HttpRequestException("offline");
        var (service, handler) = Create();

        var failure = (await service.Invoking(s => s.ImportAsync(Invocation)).Should().ThrowAsync<InvocationFailedException>()).Subject.Single();

        failure.Retryable.Should().BeTrue();
        handler.Requests.Should().Be(0);
    }

    [Fact]
    public async Task Disabled_intake_is_reported_as_terminal()
    {
        settings.Enabled = false;
        settings.Jwk = null;
        var (service, _) = Create();

        var failure = (await service.Invoking(s => s.ImportAsync(Invocation)).Should().ThrowAsync<InvocationFailedException>()).Subject.Single();

        failure.Retryable.Should().BeFalse();
    }

    [Fact]
    public async Task A_failure_after_dispatch_is_terminal_and_does_not_retry()
    {
        var (service, handler) = Create(HttpStatusCode.NotFound, "");

        var failure = (await service.Invoking(s => s.ImportAsync(Invocation)).Should().ThrowAsync<InvocationFailedException>()).Subject.Single();

        failure.Retryable.Should().BeFalse();
        handler.Requests.Should().Be(1);
    }

    [Fact]
    public async Task A_payload_that_cannot_be_imported_is_terminal_and_persists_nothing()
    {
        var (service, handler) = Create(body: "<gpx><trk><trkseg></gpx>");

        var failure = (await service.Invoking(s => s.ImportAsync(Invocation)).Should().ThrowAsync<InvocationFailedException>()).Subject.Single();

        failure.Retryable.Should().BeFalse();
        handler.Requests.Should().Be(1);
        (await routes.ListAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task An_expired_link_is_rejected_before_any_request()
    {
        clock.SetUtcNow(fixture.IssuedAt.AddMinutes(11));
        var (service, handler) = Create();

        await service.Invoking(s => s.ImportAsync(Invocation)).Should().ThrowAsync<InvocationFailedException>();

        handler.Requests.Should().Be(0);
    }
}
