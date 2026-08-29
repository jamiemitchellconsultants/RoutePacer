using System.Text;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using RoutePacer.App.Routes;
using RoutePacer.Core.Domain;
using RoutePacer.Core.Import;
using RoutePacer.Core.Storage;
using ImportRoutePage = RoutePacer.App.Pages.ImportRoute;

namespace RoutePacer.App.Tests.Pages;

public sealed class ImportRouteTests : BunitContext
{
    private const string TimedGpx = """
        <gpx version="1.1" xmlns="http://www.topografix.com/GPX/1/1"><trk><trkseg>
        <trkpt lat="0.0" lon="0.000"><time>2026-08-27T12:00:00Z</time></trkpt>
        <trkpt lat="0.0" lon="0.001"><time>2026-08-27T12:00:10Z</time></trkpt>
        <trkpt lat="0.0" lon="0.002"><time>2026-08-27T12:00:20Z</time></trkpt>
        </trkseg></trk></gpx>
        """;

    private readonly InMemoryRouteRepository routes = new();
    private readonly InMemorySettingsRepository settings = new();

    public ImportRouteTests()
    {
        Services.AddSingleton<IRouteRepository>(routes);
        Services.AddSingleton<IRideRepository>(new InMemoryRideRepository());
        Services.AddSingleton(TimeProvider.System);
        Services.AddSingleton(new RouteImportService([new GpxRouteParser(), new FitRouteParser()], new RouteNormalizer()));
        Services.AddSingleton<RouteCatalogService>();
        Services.AddSingleton<ISettingsRepository>(settings);
    }

    [Fact]
    public void The_picker_accepts_only_gpx_and_fit_and_starts_enabled()
    {
        var input = Render<ImportRoutePage>().Find("input[type=file]");

        input.GetAttribute("accept").Should().Be(".gpx,.fit");
        input.HasAttribute("disabled").Should().BeFalse();
    }

    [Fact]
    public async Task A_successful_import_persists_the_route_and_offers_start_ride()
    {
        var page = Render<ImportRoutePage>();

        page.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromText(TimedGpx, "Morning Loop.gpx"));

        var stored = await routes.GetAsync();
        stored.Should().NotBeNull();
        stored!.Summary.Name.Should().Be("Morning Loop");
        page.Markup.Should().Contain("Morning Loop").And.Contain("Timed route").And.Contain("points");
        page.Find("a[href='/track']").TextContent.Should().Contain("Start ride");
    }

    [Fact]
    public async Task A_failed_import_shows_an_actionable_message_and_saves_nothing()
    {
        var page = Render<ImportRoutePage>();

        page.FindComponent<InputFile>().UploadFiles(
            InputFileContent.CreateFromText("<gpx><trk><trkseg></gpx>", "broken.gpx"));

        page.Find("[role=alert]").TextContent.Should().Contain("Could not import that route");
        (await routes.GetAsync()).Should().BeNull();
    }

    [Fact]
    public void Each_stable_import_code_maps_to_its_own_message()
    {
        var page = Render<ImportRoutePage>();

        page.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromText(
            "<gpx><trk><trkseg><trkpt lat=\"0\" lon=\"0\"/><trkpt lat=\"0\" lon=\"0\"/></trkseg></trk></gpx>", "short.gpx"));

        page.Find("[role=alert]").TextContent.Should().Contain("at least three distinct points");
    }

    [Fact]
    public void An_unsupported_extension_is_reported()
    {
        var page = Render<ImportRoutePage>();

        page.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromText("data", "route.tcx"));

        page.Find("[role=alert]").TextContent.Should().Contain("Could not import that route");
    }

    [Fact]
    public void An_untimed_route_is_labelled_distance_only()
    {
        var page = Render<ImportRoutePage>();
        var untimed = new StringBuilder("<gpx><trk><trkseg>")
            .Append("<trkpt lat=\"0\" lon=\"0.000\"/><trkpt lat=\"0\" lon=\"0.001\"/><trkpt lat=\"0\" lon=\"0.002\"/>")
            .Append("</trkseg></trk></gpx>").ToString();

        page.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromText(untimed, "untimed.gpx"));

        page.Markup.Should().Contain("Distance-only route");
    }

    [Fact]
    public void Autopause_starts_off_with_the_default_threshold_shown()
    {
        var page = Render<ImportRoutePage>();

        page.Find("input[type=checkbox]").HasAttribute("checked").Should().BeFalse();
        var seconds = page.Find("input[type=number]");
        seconds.GetAttribute("value").Should().Be("15");
        seconds.HasAttribute("disabled").Should().BeTrue();
    }

    [Fact]
    public void Enabling_autopause_saves_it_and_frees_the_threshold()
    {
        var page = Render<ImportRoutePage>();

        page.Find("input[type=checkbox]").Change(true);

        settings.AutoPause.Enabled.Should().BeTrue();
        page.Find("input[type=number]").HasAttribute("disabled").Should().BeFalse();
    }

    [Fact]
    public void Changing_the_threshold_saves_it()
    {
        var page = Render<ImportRoutePage>();
        page.Find("input[type=checkbox]").Change(true);

        page.Find("input[type=number]").Change("45");

        settings.AutoPause.Should().Be(new AutoPauseSettings(true, 45));
    }

    [Fact]
    public void A_threshold_outside_the_accepted_range_is_clamped_before_it_is_stored()
    {
        var page = Render<ImportRoutePage>();
        page.Find("input[type=checkbox]").Change(true);

        page.Find("input[type=number]").Change("9999");

        settings.AutoPause.ThresholdSeconds.Should().Be(300);
    }

    [Fact]
    public void A_stored_preference_is_shown_when_the_page_opens()
    {
        settings.AutoPause = new AutoPauseSettings(true, 90);

        var page = Render<ImportRoutePage>();

        page.Find("input[type=checkbox]").HasAttribute("checked").Should().BeTrue();
        page.Find("input[type=number]").GetAttribute("value").Should().Be("90");
    }
}
