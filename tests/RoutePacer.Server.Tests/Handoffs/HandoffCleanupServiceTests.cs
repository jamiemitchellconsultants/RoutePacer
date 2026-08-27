using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using RoutePacer.Persistence.Handoffs;
using RoutePacer.Server.Handoffs;

namespace RoutePacer.Server.Tests.Handoffs;

public sealed class HandoffCleanupServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 27, 12, 0, 0, TimeSpan.Zero);

    private sealed class FailingStore : IHandoffStore
    {
        public int Calls { get; private set; }
        public Task InsertAsync(byte[] tokenHash, ReadOnlyMemory<byte> content, DateTimeOffset createdAt, DateTimeOffset expiresAt, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<byte[]?> ConsumeAsync(byte[] tokenHash, DateTimeOffset now, CancellationToken cancellationToken = default) => Task.FromResult<byte[]?>(null);
        public Task<int> DeleteExpiredAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
        {
            Calls++;
            throw new InvalidOperationException("Host=db;Password=secret");
        }
    }

    private static (HandoffCleanupService Service, T Store, FakeTimeProvider Clock) Create<T>(T store) where T : class, IHandoffStore
    {
        var services = new ServiceCollection().AddSingleton<IHandoffStore>(store).BuildServiceProvider();
        var clock = new FakeTimeProvider(Now);
        return (new HandoffCleanupService(services, clock, NullLogger<HandoffCleanupService>.Instance), store, clock);
    }

    [Fact]
    public async Task Cleanup_runs_once_immediately_on_start()
    {
        var (service, store, _) = Create(new RecordingHandoffStore());

        await service.StartAsync(CancellationToken.None);

        store.DeleteExpiredCount.Should().Be(1);
        await service.DisposeAsync();
    }

    [Fact]
    public async Task Cleanup_repeats_on_the_one_minute_interval()
    {
        var (service, store, clock) = Create(new RecordingHandoffStore());
        await service.StartAsync(CancellationToken.None);

        clock.Advance(HandoffCleanupService.Interval);
        clock.Advance(HandoffCleanupService.Interval);

        store.DeleteExpiredCount.Should().Be(3);
        await service.DisposeAsync();
    }

    [Fact]
    public async Task Expired_rows_are_deleted_and_unexpired_rows_are_kept()
    {
        var (service, store, clock) = Create(new RecordingHandoffStore());
        store.Seed("9Xq2mWbTf4LpN0sRvEjKcYzA7dHuG1iObQ3xVn5MtSw", "<gpx/>"u8.ToArray(), Now.AddMinutes(1));
        store.Seed("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", "<gpx/>"u8.ToArray(), Now.AddMinutes(30));

        await service.StartAsync(CancellationToken.None);
        clock.Advance(TimeSpan.FromMinutes(5));

        store.Rows.Should().ContainSingle();
        await service.DisposeAsync();
    }

    [Fact]
    public async Task A_failure_is_swallowed_and_the_next_interval_still_runs()
    {
        var (service, store, clock) = Create(new FailingStore());

        await service.StartAsync(CancellationToken.None);
        clock.Advance(HandoffCleanupService.Interval);

        store.Calls.Should().Be(2);
        await service.DisposeAsync();
    }

    [Fact]
    public async Task Stopping_prevents_any_further_run()
    {
        var (service, store, clock) = Create(new RecordingHandoffStore());
        await service.StartAsync(CancellationToken.None);

        await service.StopAsync(CancellationToken.None);
        clock.Advance(HandoffCleanupService.Interval);

        store.DeleteExpiredCount.Should().Be(1);
        await service.DisposeAsync();
    }
}
