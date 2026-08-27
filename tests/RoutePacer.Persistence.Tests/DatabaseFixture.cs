using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using RoutePacer.Persistence;
using Testcontainers.PostgreSql;

namespace RoutePacer.Persistence.Tests;

/// <summary>One real PostgreSQL 16 instance shared by the persistence suites.</summary>
public sealed class DatabaseFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer container = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("routepacer")
        .WithUsername("routepacer")
        .WithPassword("routepacer")
        .Build();

    public string ConnectionString => container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await container.StartAsync();
        await using var scope = NewProvider().CreateAsyncScope();
        await using var db = await scope.ServiceProvider.GetRequiredService<IDbContextFactory<RoutePacerDbContext>>().CreateDbContextAsync();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await container.DisposeAsync();

    /// <summary>A completely independent provider, so tests can prove two replicas share one database.</summary>
    public ServiceProvider NewProvider() => new ServiceCollection()
        .AddDbContextFactory<RoutePacerDbContext>(options => options.UseNpgsql(ConnectionString))
        .BuildServiceProvider();

    public async Task<IReadOnlyList<string>> QueryColumnsAsync(string table)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT column_name FROM information_schema.columns WHERE table_name = @table ORDER BY ordinal_position";
        command.Parameters.AddWithValue("table", table);
        var columns = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) columns.Add(reader.GetString(0));
        return columns;
    }

    public async Task<T?> ScalarAsync<T>(string sql)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        var value = await command.ExecuteScalarAsync();
        return value is null or DBNull ? default : (T)value;
    }

    public async Task TruncateAsync()
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "TRUNCATE handoffs";
        await command.ExecuteNonQueryAsync();
    }
}

[CollectionDefinition(nameof(DatabaseCollection))]
public sealed class DatabaseCollection : ICollectionFixture<DatabaseFixture>;
