using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using RoutePacer.App.Invocation;
using RoutePacer.Core.Import;

namespace RoutePacer.App.Tests.Invocation;

public sealed class HandoffPayloadClientTests
{
    private static readonly Uri Payload = new("https://pacetracking.tqaentry.com/api/handoffs/9Xq2mWbTf4LpN0sRvEjKcYzA7dHuG1iObQ3xVn5MtSw");

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public int Requests { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            return Task.FromResult(respond(request));
        }
    }

    private static HttpResponseMessage Gpx(byte[] content, string mediaType = "application/gpx+xml", long? declaredLength = null, HttpStatusCode status = HttpStatusCode.OK)
    {
        var response = new HttpResponseMessage(status) { Content = new ByteArrayContent(content) };
        response.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(mediaType);
        if (declaredLength is not null) response.Content.Headers.ContentLength = declaredLength;
        return response;
    }

    private static (HandoffPayloadClient Client, StubHandler Handler) Create(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var handler = new StubHandler(respond);
        return (new HandoffPayloadClient(new HttpClient(handler)), handler);
    }

    [Fact]
    public async Task Returns_the_exact_bytes_and_issues_one_request()
    {
        var expected = "<gpx>exact</gpx>"u8.ToArray();
        var (client, handler) = Create(_ => Gpx(expected));

        var bytes = await client.FetchOnceAsync(Payload);

        bytes.Should().Equal(expected);
        handler.Requests.Should().Be(1);
    }

    [Fact]
    public async Task Does_not_retry_after_a_failed_response()
    {
        var (client, handler) = Create(_ => Gpx([], status: HttpStatusCode.NotFound));

        await client.Invoking(c => c.FetchOnceAsync(Payload)).Should().ThrowAsync<InvalidDataException>();

        handler.Requests.Should().Be(1);
    }

    [Theory]
    [InlineData("application/xml")]
    [InlineData("text/xml")]
    [InlineData("text/plain")]
    public async Task Rejects_any_media_type_other_than_gpx(string mediaType)
    {
        var (client, _) = Create(_ => Gpx("<gpx/>"u8.ToArray(), mediaType));

        await client.Invoking(c => c.FetchOnceAsync(Payload)).Should().ThrowAsync<InvalidDataException>();
    }

    [Fact]
    public async Task Accepts_a_charset_parameter_on_the_expected_media_type()
    {
        var (client, _) = Create(_ => Gpx("<gpx/>"u8.ToArray(), "application/gpx+xml; charset=utf-8"));

        (await client.FetchOnceAsync(Payload)).Should().NotBeEmpty();
    }

    [Fact]
    public async Task Rejects_a_declared_length_above_the_maximum_without_reading_the_body()
    {
        var (client, _) = Create(_ => Gpx("<gpx/>"u8.ToArray(), declaredLength: RouteImportLimits.MaximumFileBytes + 1));

        await client.Invoking(c => c.FetchOnceAsync(Payload)).Should().ThrowAsync<InvalidDataException>();
    }

    [Fact]
    public async Task Rejects_a_body_that_exceeds_the_maximum_while_streaming()
    {
        // A false-small declared length must not let an oversized body through.
        var oversized = new byte[RouteImportLimits.MaximumFileBytes + 1];
        var (client, _) = Create(_ => Gpx(oversized, declaredLength: 16));

        await client.Invoking(c => c.FetchOnceAsync(Payload)).Should().ThrowAsync<InvalidDataException>();
    }

    [Fact]
    public async Task Rejects_an_empty_body()
    {
        var (client, _) = Create(_ => Gpx([]));

        await client.Invoking(c => c.FetchOnceAsync(Payload)).Should().ThrowAsync<InvalidDataException>();
    }

    [Fact]
    public async Task Honours_cancellation()
    {
        var (client, _) = Create(_ => Gpx("<gpx/>"u8.ToArray()));
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await client.Invoking(c => c.FetchOnceAsync(Payload, cancelled.Token)).Should().ThrowAsync<OperationCanceledException>();
    }
}
