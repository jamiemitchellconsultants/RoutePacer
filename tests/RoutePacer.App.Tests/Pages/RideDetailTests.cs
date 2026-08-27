using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using RoutePacer.Core.Domain;
using RoutePacer.Core.Storage;
using RideDetailPage = RoutePacer.App.Pages.RideDetail;

namespace RoutePacer.App.Tests.Pages;

public sealed class RideDetailTests : BunitContext
{
    private readonly InMemoryRideRepository rides = new();
    private readonly InMemoryRouteRepository routes = new();
    private static readonly DateTimeOffset Start = new(2026, 8, 27, 12, 0, 0, TimeSpan.Zero);

    public RideDetailTests()
    {
        Services.AddSingleton<IRideRepository>(rides);
        Services.AddSingleton<IRouteRepository>(routes);
    }

    [Fact]
    public void A_missing_ride_is_reported_rather_than_thrown()
    {
        var page = Render<RideDetailPage>(p => p.Add(c => c.RideId, Guid.NewGuid()));

        page.Markup.Should().Contain("Ride not found");
    }

    [Fact]
    public async Task Detail_shows_the_route_name_timestamps_aggregates_and_point_count()
    {
        var track = TrackFixtures.Straight(name: "Evening loop");
        await routes.SaveAsync(track);
        var ride = new RideSummary(Guid.NewGuid(), track.Summary.RouteId, Start, Start.AddSeconds(3723), RideStatus.Completed, 12_345, 3723, 3.3);
        await rides.CreateAsync(ride);
        await rides.AppendPointAsync(Point(ride.RideId, 0));
        await rides.AppendPointAsync(Point(ride.RideId, 1, deltaTime: -12, deltaDistance: 45, crossTrack: 3));

        var page = Render<RideDetailPage>(p => p.Add(c => c.RideId, ride.RideId));

        page.Markup.Should().Contain("Evening loop");
        page.Markup.Should().Contain(Start.ToLocalTime().ToString("g"));
        page.Markup.Should().Contain("12.35 km").And.Contain("1:02:03");
        page.Markup.Should().Contain(">2<");                 // accepted GPS points
        page.Markup.Should().Contain("0:12 ahead");           // final time delta
        page.Markup.Should().Contain("45 m behind");          // final distance delta
        page.Markup.Should().Contain("3 m off line");
    }

    [Fact]
    public async Task An_interrupted_ride_explains_its_partial_totals()
    {
        var ride = new RideSummary(Guid.NewGuid(), Guid.NewGuid(), Start, null, RideStatus.Interrupted, 100, 60, 1.7);
        await rides.CreateAsync(ride);

        var page = Render<RideDetailPage>(p => p.Add(c => c.RideId, ride.RideId));

        page.Markup.Should().Contain("interrupted before it was completed");
    }

    [Fact]
    public async Task Deletion_requires_confirmation()
    {
        var ride = new RideSummary(Guid.NewGuid(), Guid.NewGuid(), Start, null, RideStatus.Completed, 100, 60, 1.7);
        await rides.CreateAsync(ride);
        var page = Render<RideDetailPage>(p => p.Add(c => c.RideId, ride.RideId));

        page.FindAll("button").Single(b => b.TextContent.Contains("Delete ride")).Click();

        page.Markup.Should().Contain("This cannot be undone");
        rides.DeleteCount.Should().Be(0);
    }

    private static RidePoint Point(Guid rideId, long sequence, double? deltaTime = null, double? deltaDistance = null, double? crossTrack = null)
        => new(rideId, sequence, Start.AddSeconds(sequence), 0, sequence * 0.001, 8, 5, sequence * 100, deltaDistance, deltaTime, crossTrack);
}
