using FluentAssertions;
using Microsoft.Playwright;

namespace RoutePacer.E2E;

[Collection(nameof(PublishedAppCollection))]
public sealed class TrackingCapabilityTests(PublishedAppFixture app) : IAsyncLifetime
{
    private const int BootTimeoutMs = 60_000;

    private IPlaywright playwright = default!;
    private IBrowser browser = default!;

    public async Task InitializeAsync()
    {
        playwright = await Playwright.CreateAsync();
        browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
    }

    public async Task DisposeAsync()
    {
        await browser.CloseAsync();
        playwright.Dispose();
    }

    private static string GpxFixture => Path.Combine(AppContext.BaseDirectory, "Fixtures", "timed-route.gpx");

    private async Task<IBrowserContext> ContextAsync(bool grantGeolocation)
    {
        var context = await browser.NewContextAsync(new()
        {
            Permissions = grantGeolocation ? ["geolocation"] : [],
            Geolocation = new() { Latitude = 0, Longitude = 0, Accuracy = 5 },
        });
        context.SetDefaultTimeout(BootTimeoutMs);
        return context;
    }

    private async Task<(IPage Page, string RouteId)> ImportAsync(IBrowserContext context)
    {
        var page = await context.NewPageAsync();
        await page.GotoAsync($"{app.BaseUrl}/import");
        await page.WaitForSelectorAsync("input[type=file]", new() { Timeout = BootTimeoutMs });
        await page.Locator("input[type=file]").SetInputFilesAsync(GpxFixture);
        var start = page.Locator("a:has-text('Start ride')");
        await start.WaitForAsync();
        var href = await start.GetAttributeAsync("href");
        return (page, href!.Split('/')[^1]);
    }

    [Fact]
    public async Task A_ride_records_positions_and_survives_a_reload()
    {
        await using var context = await ContextAsync(grantGeolocation: true);
        var (page, routeId) = await ImportAsync(context);

        await page.GotoAsync($"{app.BaseUrl}/track/{routeId}");
        await page.Locator("button:has-text('Start ride')").ClickAsync();
        await page.Locator("button:has-text('Start ride now')").ClickAsync();
        await page.WaitForSelectorAsync("section.tracker");

        // Two mocked positions along the imported route.
        await context.SetGeolocationAsync(new() { Latitude = 0, Longitude = 0.0005f, Accuracy = 5 });
        await page.WaitForTimeoutAsync(1500);
        await context.SetGeolocationAsync(new() { Latitude = 0, Longitude = 0.0015f, Accuracy = 5 });
        await page.WaitForTimeoutAsync(1500);

        await page.Locator("button:has-text('Stop ride')").ClickAsync();
        await page.Locator("button:has-text('Stop and save')").ClickAsync();

        await page.WaitForURLAsync("**/rides");
        await page.ReloadAsync();
        await page.WaitForSelectorAsync("article.ride-card", new() { Timeout = BootTimeoutMs });

        (await page.Locator("article.ride-card").CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task The_tracker_shows_the_full_metric_set_while_running()
    {
        await using var context = await ContextAsync(grantGeolocation: true);
        var (page, routeId) = await ImportAsync(context);

        await page.GotoAsync($"{app.BaseUrl}/track/{routeId}");
        await page.Locator("button:has-text('Start ride')").ClickAsync();
        await page.Locator("button:has-text('Start ride now')").ClickAsync();
        await page.WaitForSelectorAsync("section.tracker");

        var tracker = await page.Locator("section.tracker").InnerTextAsync();
        foreach (var label in new[] { "Speed", "Elapsed", "GPS accuracy", "Line", "Progress", "Saved", "Screen" })
            tracker.Should().Contain(label);

        (await page.Locator(".pace-delta").CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task Denied_location_permission_stops_the_ride_with_recovery_guidance()
    {
        await using var context = await ContextAsync(grantGeolocation: false);
        var (page, routeId) = await ImportAsync(context);

        await page.GotoAsync($"{app.BaseUrl}/track/{routeId}");
        await page.Locator("button:has-text('Start ride')").ClickAsync();
        await page.Locator("button:has-text('Start ride now')").ClickAsync();

        await page.WaitForSelectorAsync("text=permission was denied");
        (await page.Locator("button:has-text('Start ride')").CountAsync()).Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Geolocation_is_not_requested_before_the_rider_starts_a_ride()
    {
        await using var context = await ContextAsync(grantGeolocation: false);
        var (page, routeId) = await ImportAsync(context);

        await page.GotoAsync($"{app.BaseUrl}/track/{routeId}");
        await page.WaitForSelectorAsync("button:has-text('Start ride')");

        var state = await page.EvaluateAsync<string>(
            "async () => (await navigator.permissions.query({ name: 'geolocation' })).state");

        state.Should().NotBe("granted");
        (await page.Locator("section.tracker").CountAsync()).Should().Be(0);
    }
}
