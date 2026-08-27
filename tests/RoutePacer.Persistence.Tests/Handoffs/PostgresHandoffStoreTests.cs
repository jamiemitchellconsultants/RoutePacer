using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RoutePacer.Persistence;
using RoutePacer.Persistence.Handoffs;

namespace RoutePacer.Persistence.Tests.Handoffs;

[Collection(nameof(DatabaseCollection))]
public sealed class PostgresHandoffStoreTests(DatabaseFixture database) : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 8, 27, 12, 0, 0, TimeSpan.Zero);

    private ServiceProvider provider = default!;
    private PostgresHandoffStore store = default!;

    public async Task InitializeAsync()
    {
        await database.TruncateAsync();
        provider = database.NewProvider();
        store = new PostgresHandoffStore(provider.GetRequiredService<IDbContextFactory<RoutePacerDbContext>>());
    }

    public async Task DisposeAsync() => await provider.DisposeAsync();

    [Fact]
    public async Task Migration_creates_only_the_approved_handoff_columns()
    {
        var columns = await database.QueryColumnsAsync("handoffs");

        columns.Should().BeEquivalentTo(["token_hash", "content", "created_at", "expires_at"]);
    }

    [Fact]
    public async Task The_schema_uses_the_approved_types_and_primary_key()
    {
        (await database.ScalarAsync<string>(
            "SELECT data_type FROM information_schema.columns WHERE table_name='handoffs' AND column_name='content'"))
            .Should().Be("bytea");
        (await database.ScalarAsync<string>(
            "SELECT data_type FROM information_schema.columns WHERE table_name='handoffs' AND column_name='expires_at'"))
            .Should().Be("timestamp with time zone");
        (await database.ScalarAsync<string>("""
            SELECT a.attname FROM pg_index i
            JOIN pg_attribute a ON a.attrelid = i.indrelid AND a.attnum = ANY(i.indkey)
            WHERE i.indrelid = 'handoffs'::regclass AND i.indisprimary
            """)).Should().Be("token_hash");
    }

    [Fact]
    public async Task Consumption_returns_the_exact_bytes_and_deletes_the_row()
    {
        var token = HandoffToken.Create();
        var content = "<gpx>exact bytes ⚑</gpx>"u8.ToArray();
        await store.InsertAsync(token.Sha256, content, Now, Now.AddMinutes(10));

        var consumed = await store.ConsumeAsync(token.Sha256, Now.AddMinutes(1));

        consumed.Should().Equal(content);
        (await database.ScalarAsync<long>("SELECT count(*) FROM handoffs")).Should().Be(0);
    }

    [Fact]
    public async Task A_second_consumption_returns_null()
    {
        var token = HandoffToken.Create();
        await store.InsertAsync(token.Sha256, "<gpx/>"u8.ToArray(), Now, Now.AddMinutes(10));
        await store.ConsumeAsync(token.Sha256, Now.AddMinutes(1));

        (await store.ConsumeAsync(token.Sha256, Now.AddMinutes(1))).Should().BeNull();
    }

    [Fact]
    public async Task An_expired_row_is_not_returned_and_is_left_for_cleanup()
    {
        var token = HandoffToken.Create();
        await store.InsertAsync(token.Sha256, "<gpx/>"u8.ToArray(), Now, Now.AddMinutes(10));

        (await store.ConsumeAsync(token.Sha256, Now.AddMinutes(10))).Should().BeNull();
        (await database.ScalarAsync<long>("SELECT count(*) FROM handoffs")).Should().Be(1);
    }

    [Fact]
    public async Task An_unknown_token_returns_null()
        => (await store.ConsumeAsync(HandoffToken.Create().Sha256, Now)).Should().BeNull();

    [Fact]
    public async Task Expired_rows_are_deleted_and_unexpired_rows_are_kept()
    {
        var expired = HandoffToken.Create();
        var live = HandoffToken.Create();
        await store.InsertAsync(expired.Sha256, "<gpx/>"u8.ToArray(), Now, Now.AddMinutes(1));
        await store.InsertAsync(live.Sha256, "<gpx/>"u8.ToArray(), Now, Now.AddMinutes(30));

        var deleted = await store.DeleteExpiredAsync(Now.AddMinutes(5));

        deleted.Should().Be(1);
        (await store.ConsumeAsync(live.Sha256, Now.AddMinutes(5))).Should().NotBeNull();
    }

    [Fact]
    public async Task A_large_payload_round_trips_byte_for_byte()
    {
        var token = HandoffToken.Create();
        var content = new byte[4 * 1024 * 1024];
        Random.Shared.NextBytes(content);
        await store.InsertAsync(token.Sha256, content, Now, Now.AddMinutes(10));

        (await store.ConsumeAsync(token.Sha256, Now.AddMinutes(1))).Should().Equal(content);
    }

    [Fact]
    public async Task Racing_consumers_produce_exactly_one_winner()
    {
        var token = HandoffToken.Create();
        var content = "<gpx>only once</gpx>"u8.ToArray();
        await store.InsertAsync(token.Sha256, content, Now, Now.AddMinutes(10));

        // Two independent providers, as two replicas would be.
        await using var firstProvider = database.NewProvider();
        await using var secondProvider = database.NewProvider();
        var first = new PostgresHandoffStore(firstProvider.GetRequiredService<IDbContextFactory<RoutePacerDbContext>>());
        var second = new PostgresHandoffStore(secondProvider.GetRequiredService<IDbContextFactory<RoutePacerDbContext>>());

        using var barrier = new Barrier(2);
        async Task<byte[]?> Consume(PostgresHandoffStore target)
        {
            await Task.Yield();
            barrier.SignalAndWait();
            return await target.ConsumeAsync(token.Sha256, Now.AddMinutes(1));
        }

        var results = await Task.WhenAll(Consume(first), Consume(second));

        results.Count(r => r is not null).Should().Be(1);
        results.Single(r => r is not null).Should().Equal(content);
        (await database.ScalarAsync<long>("SELECT count(*) FROM handoffs")).Should().Be(0);
    }
}
