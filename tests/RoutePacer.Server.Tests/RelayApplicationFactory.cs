using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using RoutePacer.Persistence.Handoffs;

namespace RoutePacer.Server.Tests;

/// <summary>Records what the relay stores without needing PostgreSQL.</summary>
public sealed class RecordingHandoffStore : IHandoffStore
{
    private readonly Dictionary<string, (byte[] Content, DateTimeOffset ExpiresAt)> rows = new();

    public Exception? InsertFailure { get; set; }
    public int InsertCount { get; private set; }
    public int ConsumeCount { get; private set; }
    public int DeleteExpiredCount { get; private set; }
    public IReadOnlyDictionary<string, (byte[] Content, DateTimeOffset ExpiresAt)> Rows => rows;

    private static string Key(byte[] hash) => Convert.ToHexString(hash);

    public Task InsertAsync(byte[] tokenHash, ReadOnlyMemory<byte> content, DateTimeOffset createdAt, DateTimeOffset expiresAt, CancellationToken cancellationToken = default)
    {
        InsertCount++;
        if (InsertFailure is not null) throw InsertFailure;
        rows[Key(tokenHash)] = (content.ToArray(), expiresAt);
        return Task.CompletedTask;
    }

    public Task<byte[]?> ConsumeAsync(byte[] tokenHash, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        ConsumeCount++;
        var key = Key(tokenHash);
        if (!rows.TryGetValue(key, out var row) || row.ExpiresAt <= now) return Task.FromResult<byte[]?>(null);
        rows.Remove(key);
        return Task.FromResult<byte[]?>(row.Content);
    }

    public Task<int> DeleteExpiredAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        DeleteExpiredCount++;
        var expired = rows.Where(r => r.Value.ExpiresAt <= now).Select(r => r.Key).ToArray();
        foreach (var key in expired) rows.Remove(key);
        return Task.FromResult(expired.Length);
    }

    public void Seed(string token, byte[] content, DateTimeOffset expiresAt) => rows[Key(HandoffToken.Hash(token))] = (content, expiresAt);
}

public sealed class RelayApplicationFactory : WebApplicationFactory<Program>
{
    public const string UploadCredential = "test-upload-credential";

    public RecordingHandoffStore Store { get; } = new();
    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 8, 27, 12, 0, 0, TimeSpan.Zero));

    public bool UploadsEnabled { get; init; } = true;
    public bool IntakeEnabled { get; init; }
    public string PublicKeyJwk { get; init; } = "";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("HandoffRelay:UploadsEnabled", UploadsEnabled.ToString());
        builder.UseSetting("HandoffRelay:UploadCredential", UploadCredential);
        builder.UseSetting("RouteTimerInvocation:Enabled", IntakeEnabled.ToString());
        builder.UseSetting("RouteTimerInvocation:PublicKeyJwk", PublicKeyJwk);
        builder.UseSetting("Database:ApplyMigrations", "false");

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IHandoffStore>();
            services.AddSingleton<IHandoffStore>(Store);
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
        });
    }
}
