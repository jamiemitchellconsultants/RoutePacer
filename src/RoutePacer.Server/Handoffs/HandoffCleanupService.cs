using Microsoft.Extensions.Hosting;
using RoutePacer.Persistence.Handoffs;

namespace RoutePacer.Server.Handoffs;

public sealed class HandoffCleanupService(IServiceProvider services, TimeProvider clock, ILogger<HandoffCleanupService> logger) : IHostedService, IAsyncDisposable
{
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);
    private static readonly EventId Completed = new(1000, "HandoffCleanupCompleted");
    private static readonly EventId Failed = new(1001, "HandoffCleanupFailed");

    private readonly CancellationTokenSource stopping = new();
    private readonly SemaphoreSlim gate = new(1, 1);
    private ITimer? timer;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        timer = clock.CreateTimer(static state => _ = ((HandoffCleanupService)state!).RunOnceAsync(), this, TimeSpan.Zero, Interval);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await stopping.CancelAsync();
        if (timer is not null) { await timer.DisposeAsync(); timer = null; }
    }

    internal async Task RunOnceAsync()
    {
        if (stopping.IsCancellationRequested || !await gate.WaitAsync(0, CancellationToken.None)) return;
        try
        {
            using var scope = services.CreateScope();
            var deleted = await scope.ServiceProvider.GetRequiredService<IHandoffStore>().DeleteExpiredAsync(clock.GetUtcNow(), stopping.Token);
            if (deleted > 0) logger.LogInformation(Completed, "Deleted {DeletedRows} expired handoff rows", deleted);
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested) { }
        catch (Exception ex) { logger.LogWarning(Failed, "Handoff cleanup failed with {ExceptionType}", ex.GetType().Name); }
        finally { gate.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None);
        stopping.Dispose();
        gate.Dispose();
    }
}
