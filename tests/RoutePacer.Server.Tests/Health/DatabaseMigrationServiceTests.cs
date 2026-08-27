using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using RoutePacer.Persistence;
using RoutePacer.Server.Health;

namespace RoutePacer.Server.Tests.Health;

public sealed class DatabaseMigrationServiceTests
{
    private static IServiceProvider Provider() => new ServiceCollection()
        .AddDbContextFactory<RoutePacerDbContext>(options => options.UseNpgsql("Host=127.0.0.1;Port=1;Database=absent;Username=x;Password=y;Timeout=1"))
        .BuildServiceProvider();

    private static IConfiguration Configuration(bool applyMigrations) => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["Database:ApplyMigrations"] = applyMigrations.ToString() })
        .Build();

    [Fact]
    public async Task Migrations_are_skipped_and_readiness_stays_incomplete_when_disabled()
    {
        var state = new MigrationState();
        var service = new DatabaseMigrationService(Provider(), Configuration(false), state, NullLogger<DatabaseMigrationService>.Instance);

        await service.StartAsync(CancellationToken.None);

        state.IsComplete.Should().BeFalse();
    }

    [Fact]
    public async Task An_unreachable_database_stops_startup_and_leaves_readiness_incomplete()
    {
        var state = new MigrationState();
        var service = new DatabaseMigrationService(Provider(), Configuration(true), state, NullLogger<DatabaseMigrationService>.Instance);

        var act = () => service.StartAsync(CancellationToken.None);

        await act.Should().ThrowAsync<Exception>();
        state.IsComplete.Should().BeFalse();
    }

    [Fact]
    public async Task Readiness_is_unhealthy_while_migrations_are_incomplete()
    {
        var check = new MigrationsReadyHealthCheck(new MigrationState(), Provider().GetRequiredService<IDbContextFactory<RoutePacerDbContext>>());

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Unhealthy);
    }

    [Fact]
    public async Task Readiness_is_unhealthy_when_migrations_are_complete_but_the_database_is_unreachable()
    {
        var check = new MigrationsReadyHealthCheck(new MigrationState { IsComplete = true }, Provider().GetRequiredService<IDbContextFactory<RoutePacerDbContext>>());

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Unhealthy);
    }
}
