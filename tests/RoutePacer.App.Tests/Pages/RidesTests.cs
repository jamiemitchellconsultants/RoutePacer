using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using RoutePacer.Core.Domain;
using RoutePacer.Core.Storage;
using RidesPage = RoutePacer.App.Pages.Rides;

namespace RoutePacer.App.Tests.Pages;

public sealed class RidesTests : BunitContext
{
    private readonly InMemoryRideRepository rides = new();
    private static readonly DateTimeOffset Start = new(2026, 8, 27, 12, 0, 0, TimeSpan.Zero);

    public RidesTests() => Services.AddSingleton<IRideRepository>(rides);

    [Fact]
    public void An_empty_history_says_so()
        => Render<RidesPage>().Markup.Should().Contain("No rides recorded on this device");

    [Fact]
    public async Task Rides_are_listed_newest_first()
    {
        await rides.CreateAsync(Ride(Start, RideStatus.Completed));
        await rides.CreateAsync(Ride(Start.AddDays(1), RideStatus.Interrupted));

        var page = Render<RidesPage>();

        var headings = page.FindAll("article h2 a").Select(e => e.TextContent).ToArray();
        headings.Should().HaveCount(2);
        headings[0].Should().Be(Start.AddDays(1).ToLocalTime().ToString("g"));
    }

    [Fact]
    public async Task Each_ride_shows_its_status_distance_duration_and_average()
    {
        await rides.CreateAsync(Ride(Start, RideStatus.Completed, distance: 12_345, duration: 3723, average: 3.3));

        var page = Render<RidesPage>();

        page.Markup.Should().Contain("Completed").And.Contain("12.35 km").And.Contain("1:02:03").And.Contain("11.9 km/h");
    }

    [Fact]
    public async Task An_interrupted_ride_is_explained()
    {
        await rides.CreateAsync(Ride(Start, RideStatus.Interrupted));

        Render<RidesPage>().Markup.Should().Contain("interrupted before it was completed");
    }

    [Fact]
    public async Task Deleting_asks_for_confirmation_first()
    {
        await rides.CreateAsync(Ride(Start, RideStatus.Completed));
        var page = Render<RidesPage>();

        page.FindAll("button").Single(b => b.TextContent.Contains("Delete")).Click();

        page.Markup.Should().Contain("This cannot be undone");
        rides.DeleteCount.Should().Be(0);
    }

    [Fact]
    public async Task Confirming_removes_the_ride_after_the_repository_succeeds()
    {
        await rides.CreateAsync(Ride(Start, RideStatus.Completed));
        var page = Render<RidesPage>();
        page.FindAll("button").Single(b => b.TextContent.Contains("Delete")).Click();

        page.FindAll("button").Single(b => b.TextContent.Contains("Delete ride")).Click();

        rides.DeleteCount.Should().Be(1);
        page.Markup.Should().Contain("No rides recorded on this device");
    }

    [Fact]
    public async Task Each_ride_links_to_its_detail_page()
    {
        var ride = Ride(Start, RideStatus.Completed);
        await rides.CreateAsync(ride);

        Render<RidesPage>().Find($"a[href='/rides/{ride.RideId:D}']").Should().NotBeNull();
    }

    private static RideSummary Ride(DateTimeOffset started, RideStatus status, double distance = 1000, double duration = 100, double average = 10)
        => new(Guid.NewGuid(), Guid.NewGuid(), started, started.AddSeconds(duration), status, distance, duration, average);
}
