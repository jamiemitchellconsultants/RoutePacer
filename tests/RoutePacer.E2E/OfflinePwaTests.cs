using FluentAssertions;
using Microsoft.Playwright;

namespace RoutePacer.E2E;

[Collection(nameof(PublishedAppCollection))]
public sealed class OfflinePwaTests(PublishedAppFixture app) : IAsyncLifetime
{
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

    /// <summary>WebAssembly start-up dominates every wait here, so the timeouts are deliberately generous.</summary>
    private const int BootTimeoutMs = 60_000;

    private async Task<IBrowserContext> NewContextAsync()
    {
        var context = await browser.NewContextAsync();
        context.SetDefaultTimeout(BootTimeoutMs);
        return context;
    }

    private async Task<IPage> OpenAsync(IBrowserContext context)
    {
        var page = await context.NewPageAsync();
        await page.GotoAsync(app.BaseUrl);
        await page.WaitForSelectorAsync("h1", new() { Timeout = BootTimeoutMs });
        return page;
    }

    [Fact]
    public async Task The_manifest_declares_an_installable_standalone_app_with_maskable_icons()
    {
        await using var context = await NewContextAsync();
        var page = await OpenAsync(context);

        var manifest = await page.EvaluateAsync<string>(
            "async () => await (await fetch('/manifest.webmanifest')).text()");

        manifest.Should().Contain("\"display\": \"standalone\"").And.Contain("maskable");
        (await page.Locator("link[rel=manifest]").CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task The_imported_route_is_still_available_after_going_offline()
    {
        await using var context = await NewContextAsync();
        var page = await OpenAsync(context);

        await page.GotoAsync($"{app.BaseUrl}/import");
        await page.Locator("input[type=file]").SetInputFilesAsync(GpxFixture);
        await page.WaitForSelectorAsync("text=Start ride");

        await context.SetOfflineAsync(true);
        await page.GotoAsync(app.BaseUrl);

        await page.WaitForSelectorAsync("[data-testid=route-name]");
        (await page.Locator("[data-testid=route-name]").InnerTextAsync()).Should().Be("timed-route");
    }

    [Fact]
    public async Task The_app_shell_starts_from_cache_with_no_network()
    {
        await using var context = await NewContextAsync();
        var page = await OpenAsync(context);

        // The worker must finish installing and precaching the published assets before it can serve them.
        await page.EvaluateAsync("async () => { await navigator.serviceWorker.ready; }");
        // A newly activated worker only controls the page after a navigation.
        await page.ReloadAsync();
        await page.WaitForFunctionAsync("() => navigator.serviceWorker.controller !== null", null, new() { Timeout = BootTimeoutMs });

        await context.SetOfflineAsync(true);
        await page.ReloadAsync();

        await page.WaitForSelectorAsync("h1", new() { Timeout = BootTimeoutMs });
        (await page.TitleAsync()).Should().Contain("RoutePacer");
    }

    [Fact]
    public async Task The_imported_route_survives_a_reload_because_it_lives_in_indexeddb()
    {
        await using var context = await NewContextAsync();
        var page = await OpenAsync(context);
        await page.GotoAsync($"{app.BaseUrl}/import");
        await page.Locator("input[type=file]").SetInputFilesAsync(GpxFixture);
        await page.WaitForSelectorAsync("text=Start ride");

        await page.GotoAsync(app.BaseUrl);
        await page.ReloadAsync();

        await page.WaitForSelectorAsync("[data-testid=route-name]");
        var stores = await page.EvaluateAsync<string[]>("""
            async () => {
              const db = await new Promise((resolve, reject) => {
                const request = indexedDB.open('routepacer');
                request.onsuccess = () => resolve(request.result);
                request.onerror = () => reject(request.error);
              });
              const names = Array.from(db.objectStoreNames);
              db.close();
              return names;
            }
            """);

        // The version 2 upgrade drops the ride history stores. Their absence is the schema-level
        // statement that finished rides are not kept.
        stores.Should().BeEquivalentTo(["routes", "route_points", "active_ride", "active_ride_points"]);
    }
}
