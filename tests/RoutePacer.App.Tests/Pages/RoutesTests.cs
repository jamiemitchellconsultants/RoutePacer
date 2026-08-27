using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using RoutesPage = RoutePacer.App.Pages.Routes;
using RoutePacer.App.Routes;
using RoutePacer.Core.Domain;
using RoutePacer.Core.Import;
using RoutePacer.Core.Storage;

namespace RoutePacer.App.Tests.Pages;

public sealed class RoutesTests : BunitContext
{
    private readonly InMemoryRouteRepository routes = new();
    private readonly InMemoryRideRepository rides = new();

    public RoutesTests()
    {
        Services.AddSingleton<IRouteRepository>(routes);
        Services.AddSingleton<IRideRepository>(rides);
        Services.AddSingleton(TimeProvider.System);
        Services.AddSingleton(new RouteImportService([new GpxRouteParser()], new RouteNormalizer()));
        Services.AddSingleton<RouteCatalogService>();
    }

    [Fact]
    public void An_empty_library_offers_the_import_route()
    {
        var page = Render<RoutesPage>();

        page.Markup.Should().Contain("No routes are saved yet");
        page.Find("a[href='/import']").Should().NotBeNull();
    }

    [Fact]
    public async Task Routes_are_listed_newest_import_first()
    {
        await routes.SaveAsync(Track("Older", DateTimeOffset.UnixEpoch));
        await routes.SaveAsync(Track("Newer", DateTimeOffset.UnixEpoch.AddDays(1)));

        var page = Render<RoutesPage>();

        page.FindAll("article h2").Select(e => e.TextContent).Should().Equal("Newer", "Older");
    }

    [Fact]
    public async Task A_timed_route_is_badged_differently_from_a_distance_only_route()
    {
        await routes.SaveAsync(Track("Timed", DateTimeOffset.UnixEpoch));
        await routes.SaveAsync(Track("Untimed", DateTimeOffset.UnixEpoch.AddDays(1), timed: false));

        var page = Render<RoutesPage>();

        page.Markup.Should().Contain("Timed").And.Contain("Distance only");
    }

    [Fact]
    public async Task Each_route_links_to_its_tracker()
    {
        var track = Track("Loop", DateTimeOffset.UnixEpoch);
        await routes.SaveAsync(track);

        var page = Render<RoutesPage>();

        page.Find($"a[href='/track/{track.Summary.RouteId:D}']").TextContent.Should().Contain("Start ride");
    }

    [Fact]
    public async Task Delete_asks_for_confirmation_before_removing_anything()
    {
        await routes.SaveAsync(Track("Loop", DateTimeOffset.UnixEpoch));
        var page = Render<RoutesPage>();

        page.FindAll("button").Single(b => b.TextContent.Contains("Delete")).Click();

        page.Markup.Should().Contain("This cannot be undone");
        routes.DeleteCount.Should().Be(0);
    }

    [Fact]
    public async Task Confirming_removes_the_card_only_after_the_repository_succeeds()
    {
        await routes.SaveAsync(Track("Loop", DateTimeOffset.UnixEpoch));
        var page = Render<RoutesPage>();
        page.FindAll("button").Single(b => b.TextContent.Contains("Delete")).Click();

        page.FindAll("button").Single(b => b.TextContent.Contains("Delete route")).Click();

        routes.DeleteCount.Should().Be(1);
        page.Markup.Should().Contain("No routes are saved yet");
    }

    [Fact]
    public async Task A_route_with_rides_cannot_be_deleted_and_says_why()
    {
        var track = Track("Loop", DateTimeOffset.UnixEpoch);
        await routes.SaveAsync(track);
        await rides.CreateAsync(new RideSummary(Guid.NewGuid(), track.Summary.RouteId, DateTimeOffset.UnixEpoch, null, RideStatus.Completed, 1, 1, 1));
        var page = Render<RoutesPage>();
        page.FindAll("button").Single(b => b.TextContent.Contains("Delete")).Click();

        page.FindAll("button").Single(b => b.TextContent.Contains("Delete route")).Click();

        routes.DeleteCount.Should().Be(0);
        page.Markup.Should().Contain("still has rides");
    }

    private static RouteTrack Track(string name, DateTimeOffset importedAt, bool timed = true)
    {
        var track = TrackFixtures.Straight(timed: timed, name: name);
        return new RouteTrack(track.Summary with { ImportedAtUtc = importedAt }, track.Points);
    }
}
