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

        var rebuilt = await new IndexedDbRouteRepository(module).GetAsync();

        rebuilt.Should().NotBeNull();
        rebuilt!.Summary.Should().Be(track.Summary);
        rebuilt.Points.Should().HaveCount(track.Points.Count);
        rebuilt.HasTiming.Should().BeTrue();
    }

    [Fact]
    public async Task GetAsync_returns_null_when_no_route_is_loaded()
        => (await new IndexedDbRouteRepository(new RecordingIndexedDbModule()).GetAsync()).Should().BeNull();

    // No identifier crosses the boundary in either direction: there is one route, and asking for it
    // by id would imply a choice the application does not offer.
    [Fact]
    public async Task Route_operations_take_no_identifier()
    {
        var module = new RecordingIndexedDbModule();
        var repository = new IndexedDbRouteRepository(module);

        await repository.GetAsync();
        await repository.ClearAsync();

        module.Calls.Select(c => c.Name).Should().Equal("getRoute", "clearRoute");
        module.Calls.Should().OnlyContain(c => c.Args.Length == 0);
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
    public async Task Starting_and_updating_address_the_single_active_ride()
    {
        var module = new RecordingIndexedDbModule();
        var repository = new IndexedDbRideRepository(module);
        var running = new RideSummary(Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UnixEpoch, null, RideStatus.Running, 0, 0, 0);

        await repository.StartAsync(running);
        await repository.SaveAsync(running with { Status = RideStatus.Paused });

        module.Calls.Select(c => c.Name).Should().Equal("startRide", "saveActiveRide");
        ((RideSummary)module.Calls[^1].Args[0]!).Status.Should().Be(RideStatus.Paused);
    }

    // Stopping discards the ride. If this ever became a write, a finished ride would start being
    // kept again, which is the thing privacy.md now promises does not happen.
    [Fact]
    public async Task Stopping_clears_the_active_ride_and_carries_nothing_with_it()
    {
        var module = new RecordingIndexedDbModule();

        await new IndexedDbRideRepository(module).ClearAsync();

        module.Calls.Should().ContainSingle(c => c.Name == "clearRide").Which.Args.Should().BeEmpty();
    }

    [Fact]
    public async Task Autopause_defaults_to_off_when_nothing_is_stored()
    {
        var settings = await new IndexedDbSettingsRepository(new RecordingIndexedDbModule()).GetAutoPauseAsync();

        settings.Should().Be(AutoPauseSettings.Default);
        settings.Enabled.Should().BeFalse();
        settings.ThresholdSeconds.Should().Be(15);
    }

    [Fact]
    public async Task A_stored_autopause_preference_is_read_back()
    {
        var module = new RecordingIndexedDbModule();
        module.Results["getAutoPause"] = new IndexedDbSettingsRepository.AutoPauseDto(true, 45);

        var settings = await new IndexedDbSettingsRepository(module).GetAutoPauseAsync();

        settings.Should().Be(new AutoPauseSettings(true, 45));
    }

    // A hand-edited store must not produce a threshold that never fires or fires instantly.
    [Theory]
    [InlineData(0, 5)]
    [InlineData(4, 5)]
    [InlineData(9999, 300)]
    public async Task A_threshold_outside_the_accepted_range_is_clamped_on_read(int stored, int expected)
    {
        var module = new RecordingIndexedDbModule();
        module.Results["getAutoPause"] = new IndexedDbSettingsRepository.AutoPauseDto(true, stored);

        (await new IndexedDbSettingsRepository(module).GetAutoPauseAsync()).ThresholdSeconds.Should().Be(expected);
    }

    [Fact]
    public async Task Saving_autopause_clamps_before_it_reaches_storage()
    {
        var module = new RecordingIndexedDbModule();

        await new IndexedDbSettingsRepository(module).SaveAutoPauseAsync(new AutoPauseSettings(true, 1000));

        var call = module.Calls.Should().ContainSingle(c => c.Name == "saveAutoPause").Subject;
        call.Args.Should().ContainSingle().Which.Should().Be(new AutoPauseSettings(true, 300));
    }
}
