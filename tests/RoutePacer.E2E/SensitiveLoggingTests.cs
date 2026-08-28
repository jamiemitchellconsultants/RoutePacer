using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using RoutePacer.Persistence.Handoffs;

namespace RoutePacer.E2E;

/// <summary>
/// Drives success and failure paths with recognisable canaries, writes the complete captured application
/// log to artifacts/test-logs, and asserts that none of the canaries reached it.
/// </summary>
public sealed class SensitiveLoggingTests
{
    private const string CredentialCanary = "relay-credential-canary";
    private const string RouteNameCanary = "route-name-canary";
    private const string GpxCanary = "gpx-log-canary";

    private sealed class CapturingProvider : ILoggerProvider
    {
        public List<string> Lines { get; } = [];
        public ILogger CreateLogger(string categoryName) => new Capture(this, categoryName);
        public void Dispose() { }

        private sealed class Capture(CapturingProvider owner, string category) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull
            {
                lock (owner.Lines) owner.Lines.Add($"SCOPE {category}: {state}");
                return null;
            }

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                lock (owner.Lines)
                {
                    owner.Lines.Add($"{logLevel} {category} [{eventId.Id}] {formatter(state, exception)}");
                    if (exception is not null) owner.Lines.Add(exception.ToString());
                }
            }
        }
    }

    private sealed class ThrowingStore : IHandoffStore
    {
        public Dictionary<string, byte[]> Rows { get; } = [];
        public bool Fail { get; set; }

        public Task InsertAsync(byte[] tokenHash, ReadOnlyMemory<byte> content, DateTimeOffset createdAt, DateTimeOffset expiresAt, CancellationToken cancellationToken = default)
        {
            if (Fail) throw new InvalidOperationException($"store failure carrying {GpxCanary}");
            Rows[Convert.ToHexString(tokenHash)] = content.ToArray();
            return Task.CompletedTask;
        }

        public Task<byte[]?> ConsumeAsync(byte[] tokenHash, DateTimeOffset now, CancellationToken cancellationToken = default)
        {
            var key = Convert.ToHexString(tokenHash);
            if (!Rows.Remove(key, out var content)) return Task.FromResult<byte[]?>(null);
            return Task.FromResult<byte[]?>(content);
        }

        public Task<int> DeleteExpiredAsync(DateTimeOffset now, CancellationToken cancellationToken = default) => Task.FromResult(0);
    }

    private sealed class Factory(CapturingProvider capture, ThrowingStore store) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("HandoffRelay:UploadsEnabled", "true");
            builder.UseSetting("HandoffRelay:UploadCredential", CredentialCanary);
            builder.UseSetting("Database:ApplyMigrations", "false");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IHandoffStore>();
                services.AddSingleton<IHandoffStore>(store);
            });
            builder.ConfigureLogging(logging =>
            {
                logging.SetMinimumLevel(LogLevel.Trace);
                logging.AddProvider(capture);
            });
        }
    }

    [Fact]
    public async Task No_credential_token_url_name_query_or_gpx_reaches_the_application_log()
    {
        var capture = new CapturingProvider();
        var store = new ThrowingStore();
        using var factory = new Factory(capture, store);
        using var client = factory.CreateClient();

        // Success path: authenticated upload, then a first and second fetch.
        var upload = new HttpRequestMessage(HttpMethod.Post, "/api/handoffs")
        {
            Content = new StringContent($"<gpx><name>{RouteNameCanary}</name><trkpt>{GpxCanary}</trkpt></gpx>")
        };
        upload.Content.Headers.ContentType = new MediaTypeHeaderValue("application/gpx+xml");
        upload.Headers.Authorization = new AuthenticationHeaderValue("Bearer", CredentialCanary);

        var created = await client.SendAsync(upload);
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var payloadUrl = (await created.Content.ReadFromJsonAsync<Server.Handoffs.HandoffCreatedResponse>())!.PayloadUrl;
        var token = payloadUrl.Split('/')[^1];

        await client.GetAsync($"/api/handoffs/{token}");
        await client.GetAsync($"/api/handoffs/{token}");

        // Failure paths: bad credential, wrong media type, unknown token, a signed /open query, and a store failure.
        var unauthorized = new HttpRequestMessage(HttpMethod.Post, "/api/handoffs") { Content = new StringContent("<gpx/>") };
        unauthorized.Content.Headers.ContentType = new MediaTypeHeaderValue("application/gpx+xml");
        unauthorized.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "wrong-" + CredentialCanary);
        await client.SendAsync(unauthorized);

        await client.GetAsync("/api/handoffs/AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA");
        await client.GetAsync($"/open?src=rt&v=1&payload={Uri.EscapeDataString(payloadUrl)}&name={Uri.EscapeDataString(RouteNameCanary)}&ts=1787832000000&sig={new string('A', 86)}");

        store.Fail = true;
        var failing = new HttpRequestMessage(HttpMethod.Post, "/api/handoffs") { Content = new StringContent($"<gpx>{GpxCanary}</gpx>") };
        failing.Content.Headers.ContentType = new MediaTypeHeaderValue("application/gpx+xml");
        failing.Headers.Authorization = new AuthenticationHeaderValue("Bearer", CredentialCanary);
        await client.SendAsync(failing);

        var log = string.Join(Environment.NewLine, capture.Lines);
        var directory = RepositoryRoot.Combine("artifacts", "test-logs");
        Directory.CreateDirectory(directory);
        foreach (var stale in Directory.EnumerateFiles(directory)) File.Delete(stale);
        var path = Path.Combine(directory, "sensitive-logging.log");
        await File.WriteAllTextAsync(path, log);

        new FileInfo(path).Length.Should().BeGreaterThan(0, "the run must actually produce logs to assert against");
        log.Should().NotContain(CredentialCanary);
        log.Should().NotContain(RouteNameCanary);
        log.Should().NotContain(GpxCanary);
        log.Should().NotContain(token);
        log.Should().NotContain(payloadUrl);
        log.Should().NotContain("sig=");

        // The safe filter still records the shape of the traffic.
        log.Should().Contain("/api/handoffs/{token}");
    }
}
