using Microsoft.Extensions.Hosting;
using RoutePacer.Persistence.Handoffs;

namespace RoutePacer.Server.Handoffs;

public sealed class HandoffCleanupService(IServiceProvider services, TimeProvider clock, ILogger<HandoffCleanupService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { using var scope = services.CreateScope(); var store = scope.ServiceProvider.GetRequiredService<IHandoffStore>(); await store.DeleteExpiredAsync(clock.GetUtcNow(), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogWarning(new EventId(1001, "HandoffCleanupFailed"), "Handoff cleanup failed with {ExceptionType}", ex.GetType().Name); }
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }
}
