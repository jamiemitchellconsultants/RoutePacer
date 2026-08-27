using FluentAssertions;
using RoutePacer.App.Storage;
using RoutePacer.Core.Domain;

namespace RoutePacer.App.Tests.Storage;

public sealed class IndexedDbRepositoryContractTests
{
    [Fact]
    public async Task SaveAsync_sends_route_and_points_as_one_transaction()
    {
        var module = new RecordingIndexedDbModule();
        var repository = new IndexedDbRouteRepository(module);
        var track = TrackFixtures.Straight();

        await repository.SaveAsync(track);

        var call = module.Calls.Should().ContainSingle(c => c.Name == "saveRoute").Subject;
        call.Args.Should().HaveCount(2);
        call.Args[0].Should().Be(track.Summary);
        call.Args[1].Should().BeSameAs(track.Points);
    }

    [Fact]
    public async Task GetAsync_rebuilds_a_valid_route_track()
    {
        var track = TrackFixtures.Straight();
        var module = new RecordingIndexedDbModule();
        module.Results["getRoute"] = new IndexedDbRouteRepository.RouteDto(track.Summary, [.. track.Points]);

        var rebuilt = await new IndexedDbRouteRepository(module).GetAsync(track.Summary.RouteId);

        rebuilt.Should().NotBeNull();
        rebuilt!.Summary.Should().Be(track.Summary);
        rebuilt.Points.Should().HaveCount(track.Points.Count);
        rebuilt.HasTiming.Should().BeTrue();
    }

    [Fact]
    public async Task GetAsync_returns_null_when_the_route_is_absent()
        => (await new IndexedDbRouteRepository(new RecordingIndexedDbModule()).GetAsync(Guid.NewGuid())).Should().BeNull();

    [Fact]
    public async Task ListAsync_returns_an_empty_list_rather_than_null()
        => (await new IndexedDbRouteRepository(new RecordingIndexedDbModule()).ListAsync()).Should().BeEmpty();

    [Fact]
    public async Task Route_ids_cross_the_boundary_as_lowercase_dashed_strings()
    {
        var module = new RecordingIndexedDbModule();
        var routeId = Guid.NewGuid();

        await new IndexedDbRouteRepository(module).DeleteAsync(routeId);

        module.Calls.Should().ContainSingle(c => c.Name == "deleteRoute")
            .Which.Args[0].Should().Be(routeId.ToString("D"));
    }

    [Fact]
    public async Task Ride_append_preserves_the_supplied_sequence()
    {
        var module = new RecordingIndexedDbModule();
        var repository = new IndexedDbRideRepository(module);
        var rideId = Guid.NewGuid();

        for (var sequence = 0; sequence < 3; sequence++)
            await repository.AppendPointAsync(new RidePoint(rideId, sequence, DateTimeOffset.UnixEpoch, 0, 0, null, 5, null, null, null, null));

        module.Calls.Where(c => c.Name == "appendRidePoint")
            .Select(c => ((RidePoint)c.Args[0]!).Sequence)
            .Should().Equal(0, 1, 2);
    }

    [Fact]
    public async Task Completion_replaces_the_running_summary_in_the_rides_store()
    {
        var module = new RecordingIndexedDbModule();
        var repository = new IndexedDbRideRepository(module);
        var running = new RideSummary(Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UnixEpoch, null, RideStatus.Running, 0, 0, 0);

        await repository.CreateAsync(running);
        await repository.CompleteAsync(running with { Status = RideStatus.Completed, EndedAtUtc = DateTimeOffset.UnixEpoch.AddHours(1) });

        module.Calls.Select(c => c.Name).Should().Equal("createRide", "completeRide");
        ((RideSummary)module.Calls[^1].Args[0]!).Status.Should().Be(RideStatus.Completed);
    }

    [Fact]
    public async Task Ride_deletion_addresses_the_ride_by_id()
    {
        var module = new RecordingIndexedDbModule();
        var rideId = Guid.NewGuid();

        await new IndexedDbRideRepository(module).DeleteAsync(rideId);

        module.Calls.Should().ContainSingle(c => c.Name == "deleteRide")
            .Which.Args[0].Should().Be(rideId.ToString("D"));
    }
}
