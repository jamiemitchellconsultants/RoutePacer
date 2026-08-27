using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RoutePacer.Persistence;
using RoutePacer.Persistence.Handoffs;

namespace RoutePacer.Persistence.Tests.Handoffs;

[Collection(nameof(DatabaseCollection))]
public sealed class HandoffReplicaTests(DatabaseFixture database) : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 8, 27, 12, 0, 0, TimeSpan.Zero);

    public async Task InitializeAsync() => await database.TruncateAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private static PostgresHandoffStore Store(ServiceProvider provider)
        => new(provider.GetRequiredService<IDbContextFactory<RoutePacerDbContext>>());

    [Fact]
    public async Task A_payload_inserted_by_one_replica_is_consumable_by_another()
    {
        var token = HandoffToken.Create();
        var content = "<gpx>shared</gpx>"u8.ToArray();

        await using (var uploader = database.NewProvider())
            await Store(uploader).InsertAsync(token.Sha256, content, Now, Now.AddMinutes(10));

        await using var consumer = database.NewProvider();
        (await Store(consumer).ConsumeAsync(token.Sha256, Now.AddMinutes(1))).Should().Equal(content);
    }

    [Fact]
    public async Task An_unexpired_row_survives_a_new_connection_pool()
    {
        var token = HandoffToken.Create();

        await using (var writer = database.NewProvider())
            await Store(writer).InsertAsync(token.Sha256, "<gpx>durable</gpx>"u8.ToArray(), Now, Now.AddMinutes(10));

        // A fresh provider opens new physical connections, proving the row is committed, not session state.
        await using var reader = database.NewProvider();
        (await database.ScalarAsync<long>("SELECT count(*) FROM handoffs")).Should().Be(1);
        (await Store(reader).ConsumeAsync(token.Sha256, Now.AddMinutes(9))).Should().NotBeNull();
    }
}
