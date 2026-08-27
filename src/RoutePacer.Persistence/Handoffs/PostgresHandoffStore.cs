using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace RoutePacer.Persistence.Handoffs;

public sealed class PostgresHandoffStore(IDbContextFactory<RoutePacerDbContext> contextFactory) : IHandoffStore
{
    public async Task InsertAsync(byte[] tokenHash, ReadOnlyMemory<byte> content, DateTimeOffset createdAt, DateTimeOffset expiresAt, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await db.Database.ExecuteSqlRawAsync("INSERT INTO handoffs (token_hash, content, created_at, expires_at) VALUES ({0}, {1}, {2}, {3})", [tokenHash, content.ToArray(), createdAt, expiresAt], cancellationToken);
    }

    public async Task<byte[]?> ConsumeAsync(byte[] tokenHash, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = "DELETE FROM handoffs WHERE token_hash = @token_hash AND expires_at > @now RETURNING content;";
        command.Parameters.Add(new NpgsqlParameter("token_hash", tokenHash));
        command.Parameters.Add(new NpgsqlParameter("now", now));
        await db.Database.OpenConnectionAsync(cancellationToken);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is byte[] bytes ? bytes : null;
    }

    public async Task<int> DeleteExpiredAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Database.ExecuteSqlRawAsync("DELETE FROM handoffs WHERE expires_at <= {0}", [now], cancellationToken);
    }
}
