using Microsoft.EntityFrameworkCore;
using RoutePacer.Persistence;

namespace RoutePacer.Server.Health;

public sealed class DatabaseMigrationService(IServiceProvider services, IConfiguration configuration, MigrationState state, ILogger<DatabaseMigrationService> logger) : IHostedService
{
    // A fixed key so every replica serializes on the same PostgreSQL session lock while migrating.
    private const long MigrationLockId = 5_243_071_026_001L;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!configuration.GetValue("Database:ApplyMigrations", false)) return;
        await using var scope = services.CreateAsyncScope();
        await using var db = await scope.ServiceProvider.GetRequiredService<IDbContextFactory<RoutePacerDbContext>>().CreateDbContextAsync(cancellationToken);
        try
        {
            await db.Database.OpenConnectionAsync(cancellationToken);
            try
            {
                await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_lock({0})", [MigrationLockId], cancellationToken);
                await db.Database.MigrateAsync(cancellationToken);
                state.IsComplete = true;
            }
            finally
            {
                try { await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_unlock({0})", [MigrationLockId], CancellationToken.None); }
                catch (Exception ex) { logger.LogWarning(new EventId(1101, "DatabaseMigrationUnlockFailed"), "Releasing the migration lock failed with {ExceptionType}", ex.GetType().Name); }
                await db.Database.CloseConnectionAsync();
            }
        }
        catch (Exception ex)
        {
            // Readiness stays unhealthy and startup stops rather than serving traffic against an unmigrated schema.
            logger.LogError(new EventId(1100, "DatabaseMigrationFailed"), "Database migration failed with {ExceptionType}", ex.GetType().Name);
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
