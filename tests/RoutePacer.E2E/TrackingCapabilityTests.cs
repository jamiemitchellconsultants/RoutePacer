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

    private async Task<IPage> ImportAsync(IBrowserContext context)
    {
        var page = await context.NewPageAsync();
        await page.GotoAsync($"{app.BaseUrl}/import");
        await page.WaitForSelectorAsync("input[type=file]", new() { Timeout = BootTimeoutMs });
        await page.Locator("input[type=file]").SetInputFilesAsync(GpxFixture);
        await page.Locator("a:has-text('Start ride')").WaitForAsync();
        return page;
    }

    [Fact]
    public async Task Stopping_a_ride_keeps_nothing()
    {
        await using var context = await ContextAsync(grantGeolocation: true);
        var page = await ImportAsync(context);

        await page.GotoAsync($"{app.BaseUrl}/track");
        await page.Locator("button:has-text('Start ride')").ClickAsync();
        await page.Locator("button:has-text('Start ride now')").ClickAsync();
        await page.WaitForSelectorAsync("section.tracker");

        // Two mocked positions along the imported route.
        await context.SetGeolocationAsync(new() { Latitude = 0, Longitude = 0.0005f, Accuracy = 5 });
        await page.WaitForTimeoutAsync(1500);
        await context.SetGeolocationAsync(new() { Latitude = 0, Longitude = 0.0015f, Accuracy = 5 });
        await page.WaitForTimeoutAsync(1500);

        await page.Locator("button:has-text('Stop ride')").ClickAsync();
        await page.Locator("button:has-text('Stop ride now')").ClickAsync();

        // The finished ride is readable on the page it ended on, and nowhere else.
        await page.WaitForSelectorAsync(".ride-complete", new() { Timeout = BootTimeoutMs });

        var stored = await page.EvaluateAsync<int>("""
            async () => {
              const db = await new Promise((resolve, reject) => {
                const request = indexedDB.open('routepacer');
                request.onsuccess = () => resolve(request.result);
                request.onerror = () => reject(request.error);
              });
              const count = await new Promise((resolve, reject) => {
                const r = db.transaction('active_ride').objectStore('active_ride').count();
                r.onsuccess = () => resolve(r.result);
                r.onerror = () => reject(r.error);
              });
              db.close();
              return count;
            }
            """);

        stored.Should().Be(0, "a finished ride is cleared, not stored");
    }

    // The battery argument for this app is that an OLED pixel showing black draws no power, and the
    // background is nearly all of the pixels. Asserting the computed colour keeps that a property of
    // the app rather than an intention in a stylesheet.
    [Fact]
    public async Task The_tracker_paints_a_true_black_background()
    {
        await using var context = await ContextAsync(grantGeolocation: true);
        var page = await ImportAsync(context);

        await page.GotoAsync($"{app.BaseUrl}/track");
        await page.WaitForSelectorAsync(".tracker-page", new() { Timeout = BootTimeoutMs });

        var colours = await page.EvaluateAsync<string[]>("""
            () => {
              const body = getComputedStyle(document.body).backgroundColor;
              const page = getComputedStyle(document.querySelector('.tracker-page')).backgroundColor;
              return [body, page];
            }
            """);

        colours.Should().AllSatisfy(c => c.Should().BeOneOf("rgb(0, 0, 0)", "rgba(0, 0, 0, 1)"));
    }

    [Fact]
    public async Task The_tracker_shows_the_full_metric_set_while_running()
    {
        await using var context = await ContextAsync(grantGeolocation: true);
        var page = await ImportAsync(context);

        await page.GotoAsync($"{app.BaseUrl}/track");
        await page.Locator("button:has-text('Start ride')").ClickAsync();
        await page.Locator("button:has-text('Start ride now')").ClickAsync();
        await page.WaitForSelectorAsync("section.tracker");

        var tracker = await page.Locator("section.tracker").InnerTextAsync();
        foreach (var label in new[] { "Speed", "Elapsed", "GPS accuracy", "Line", "Progress", "Points", "Screen" })
            tracker.Should().Contain(label);

        (await page.Locator(".pace-delta").CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task Denied_location_permission_stops_the_ride_with_recovery_guidance()
    {
        await using var context = await ContextAsync(grantGeolocation: false);
        var page = await ImportAsync(context);

        await page.GotoAsync($"{app.BaseUrl}/track");
        await page.Locator("button:has-text('Start ride')").ClickAsync();
        await page.Locator("button:has-text('Start ride now')").ClickAsync();

        await page.WaitForSelectorAsync("text=permission was denied");
        (await page.Locator("button:has-text('Start ride')").CountAsync()).Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Geolocation_is_not_requested_before_the_rider_starts_a_ride()
    {
        await using var context = await ContextAsync(grantGeolocation: false);
        var page = await ImportAsync(context);

        await page.GotoAsync($"{app.BaseUrl}/track");
        await page.WaitForSelectorAsync("button:has-text('Start ride')");

        var state = await page.EvaluateAsync<string>(
            "async () => (await navigator.permissions.query({ name: 'geolocation' })).state");

        state.Should().NotBe("granted");
        (await page.Locator("section.tracker").CountAsync()).Should().Be(0);
    }
}
