using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using RoutePacer.Persistence;

namespace RoutePacer.Server.Health;

public sealed class MigrationsReadyHealthCheck(MigrationState state, IDbContextFactory<RoutePacerDbContext> contexts) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (!state.IsComplete) return HealthCheckResult.Unhealthy("Database migrations are not complete.");
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        return await db.Database.CanConnectAsync(cancellationToken) ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy("Database is unavailable.");
    }
}
