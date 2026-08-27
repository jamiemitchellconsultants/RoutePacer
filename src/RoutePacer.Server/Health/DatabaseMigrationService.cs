using Microsoft.EntityFrameworkCore;
using RoutePacer.Persistence;

namespace RoutePacer.Server.Health;

public sealed class DatabaseMigrationService(IServiceProvider services, IConfiguration configuration, MigrationState state, ILogger<DatabaseMigrationService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!configuration.GetValue("Database:ApplyMigrations", false)) return;
        try
        {
            await using var scope = services.CreateAsyncScope();
            await using var db = await scope.ServiceProvider.GetRequiredService<IDbContextFactory<RoutePacerDbContext>>().CreateDbContextAsync(cancellationToken);
            await db.Database.MigrateAsync(cancellationToken);
            state.IsComplete = true;
        }
        catch (Exception ex)
        {
            logger.LogError(new EventId(1100, "DatabaseMigrationFailed"), "Database migration failed with {ExceptionType}", ex.GetType().Name);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
