using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using RoutePacer.App.Browser;
using RoutePacer.App.Rides;
using RoutePacer.Core.Domain;
using RoutePacer.Core.Storage;
using TrackPage = RoutePacer.App.Pages.Track;

namespace RoutePacer.App.Tests.Pages;

public sealed class TrackTests : BunitContext
{
    private static readonly DateTimeOffset Start = new(2026, 8, 27, 12, 0, 0, TimeSpan.Zero);

    private readonly InMemoryRouteRepository routes = new();
    private readonly InMemoryRideRepository rides = new();
    private readonly FakeLocationService location = new();
    private readonly FakeWakeLockService wakeLock = new();
    private readonly FakeTimeProvider clock = new(Start);
    private readonly InMemorySettingsRepository settings = new();
    private readonly RideSessionService session;

    public TrackTests()
    {
        session = new RideSessionService(routes, rides, location, wakeLock, settings, clock);
        Services.AddSingleton<IRouteRepository>(routes);
        Services.AddSingleton<IRideRepository>(rides);
        Services.AddSingleton<ISettingsRepository>(settings);
        Services.AddSingleton(session);
    }

    private async Task<RouteTrack> Seed(bool timed = true)
    {
        var track = TrackFixtures.Straight(timed: timed, name: "Test route");
        await routes.SaveAsync(track);
        return track;
    }

    [Fact]
    public async Task An_idle_tracker_asks_for_confirmation_before_requesting_location()
    {
        var track = await Seed();
        var page = Render<TrackPage>();

        page.Markup.Should().Contain("Ready to ride");
        page.Find("button").Click();

        page.Markup.Should().Contain("asks for location access");
        location.StartCount.Should().Be(0);
    }

    [Fact]
    public async Task Confirming_starts_the_ride_and_renders_the_metric_hierarchy()
    {
        var track = await Seed();
        var page = Render<TrackPage>();
        page.Find("button").Click();

        page.FindAll("button").Single(b => b.TextContent.Contains("Start ride now")).Click();

        location.StartCount.Should().Be(1);
        page.Markup.Should().Contain("Speed").And.Contain("Elapsed").And.Contain("GPS accuracy")
            .And.Contain("Line").And.Contain("Progress").And.Contain("Points").And.Contain("Screen");
        // The time delta is the largest, primary tile and the distance delta comes second.
        var tiles = page.FindAll(".pace-delta");
        tiles.Should().HaveCount(2);
        tiles[0].ClassList.Should().Contain("pace-delta-primary");
        tiles[0].TextContent.Should().Contain("Time");
        tiles[1].TextContent.Should().Contain("Distance");
    }

    [Fact]
    public async Task A_distance_only_route_replaces_the_time_tile_with_an_explanation()
    {
        var track = await Seed(timed: false);
        var page = Render<TrackPage>();
        page.Find("button").Click();
        page.FindAll("button").Single(b => b.TextContent.Contains("Start ride now")).Click();

        var timeTile = page.FindAll(".pace-delta")[0];
        timeTile.TextContent.Should().Contain("Timing unavailable").And.Contain("distance only");
        timeTile.TextContent.Should().NotContain("On pace");
    }

    [Fact]
    public async Task Lead_and_lag_carry_a_word_as_well_as_a_tone()
    {
        var track = await Seed();
        var page = Render<TrackPage>();
        page.Find("button").Click();
        page.FindAll("button").Single(b => b.TextContent.Contains("Start ride now")).Click();

        // 500 m along a 10 m/s route is due at 50 s; arriving at 20 s is 30 s ahead.
        clock.Advance(TimeSpan.FromSeconds(20));
        await page.InvokeAsync(() => location.PushAsync(new GeoFix(Start.AddSeconds(20), 0, 0.0045, 5, 12)));

        var timeTile = page.FindAll(".pace-delta")[0];
        timeTile.TextContent.Should().Contain("ahead");
        timeTile.ClassList.Should().Contain("pace-delta-ahead");
    }

    [Fact]
    public async Task Stopping_requires_confirmation()
    {
        var track = await Seed();
        var page = Render<TrackPage>();
        page.Find("button").Click();
        page.FindAll("button").Single(b => b.TextContent.Contains("Start ride now")).Click();

        page.FindAll("button").Single(b => b.TextContent.Contains("Stop ride")).Click();

        page.Markup.Should().Contain("Stop this ride?");
        session.State.Should().Be(RideSessionState.Running);
    }

    [Fact]
    public async Task Pausing_and_resuming_toggles_the_command_label()
    {
        var track = await Seed();
        var page = Render<TrackPage>();
        page.Find("button").Click();
        page.FindAll("button").Single(b => b.TextContent.Contains("Start ride now")).Click();

        page.FindAll("button").Single(b => b.TextContent.Contains("Pause")).Click();
        page.Markup.Should().Contain("Resume");

        page.FindAll("button").Single(b => b.TextContent.Contains("Resume")).Click();
        page.Markup.Should().Contain("Pause");
    }

    [Fact]
    public async Task A_transient_gps_failure_is_surfaced_without_ending_the_ride()
    {
        var track = await Seed();
        var page = Render<TrackPage>();
        page.Find("button").Click();
        page.FindAll("button").Single(b => b.TextContent.Contains("Start ride now")).Click();

        await page.InvokeAsync(() => location.FailAsync(LocationFailure.Timeout));

        page.Markup.Should().Contain("Waiting for a GPS fix");
        page.FindAll("button").Should().Contain(b => b.TextContent.Contains("Pause"));
    }

    [Fact]
    public async Task Disposing_the_page_does_not_stop_the_ride()
    {
        var track = await Seed();
        var page = Render<TrackPage>();
        page.Find("button").Click();
        page.FindAll("button").Single(b => b.TextContent.Contains("Start ride now")).Click();

        page.Instance.Dispose();

        session.State.Should().Be(RideSessionState.Running);
        location.Watching.Should().BeTrue();
    }

    [Fact]
    public async Task The_tracker_returns_to_the_start_view_once_a_ride_is_faulted()
    {
        var track = await Seed();
        var page = Render<TrackPage>();
        page.Find("button").Click();
        page.FindAll("button").Single(b => b.TextContent.Contains("Start ride now")).Click();

        await page.InvokeAsync(() => location.FailAsync(LocationFailure.PermissionDenied));

        page.Markup.Should().Contain("Ready to ride").And.Contain("permission was denied");
    }
}
