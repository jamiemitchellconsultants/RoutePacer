using System.Text.Json;
using FluentAssertions;
using Microsoft.Playwright;

namespace RoutePacer.E2E;

/// <summary>
/// Contract v1 pins the origin to https://pacetracking.tqaentry.com as a code constant, so a link served
/// from a loopback test host can never satisfy the parser. These tests therefore exercise the two halves
/// that a browser can prove locally: real Web Crypto verification of the frozen fixture through
/// invocation.js, and the /open page's rejection, URL cleanup, and manual-import fallback.
/// The full signed flow against the production origin is covered by the manual validation matrix.
/// </summary>
[Collection(nameof(PublishedAppCollection))]
public sealed class RouteTimerInvocationBrowserTests(PublishedAppFixture app) : IAsyncLifetime
{
    private const int BootTimeoutMs = 60_000;

    private IPlaywright playwright = default!;
    private IBrowser browser = default!;
    private JsonElement fixture;

    public async Task InitializeAsync()
    {
        playwright = await Playwright.CreateAsync();
        browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
        fixture = JsonDocument.Parse(await File.ReadAllTextAsync(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "route-timer-contract-v1.json"))).RootElement.Clone();
    }

    public async Task DisposeAsync()
    {
        await browser.CloseAsync();
        playwright.Dispose();
    }

    private async Task<IPage> OpenAsync(IBrowserContext context, string path = "/")
    {
        var page = await context.NewPageAsync();
        await page.GotoAsync($"{app.BaseUrl}{path}");
        await page.WaitForSelectorAsync("h1", new() { Timeout = BootTimeoutMs });
        return page;
    }

    private async Task<bool> VerifyAsync(IPage page, string signature, string canonicalText)
    {
        var jwk = fixture.GetProperty("publicJwk").GetRawText();
        return await page.EvaluateAsync<bool>("""
            async ([jwk, signature, canonicalText]) => {
              const module = await import('/js/invocation.js');
              const bytes = new TextEncoder().encode(canonicalText);
              return await module.verifySignature(jwk, signature, bytes);
            }
            """, new object[] { jwk, signature, canonicalText });
    }

    [Fact]
    public async Task Web_crypto_verifies_the_frozen_fixture_signature()
    {
        await using var context = await browser.NewContextAsync();
        var page = await OpenAsync(context);

        var verified = await VerifyAsync(page, fixture.GetProperty("signature").GetString()!, fixture.GetProperty("canonicalText").GetString()!);

        verified.Should().BeTrue();
    }

    [Fact]
    public async Task Web_crypto_rejects_tampered_canonical_bytes()
    {
        await using var context = await browser.NewContextAsync();
        var page = await OpenAsync(context);
        var tampered = fixture.GetProperty("canonicalText").GetString()!.Replace("Café", "Cafe");

        (await VerifyAsync(page, fixture.GetProperty("signature").GetString()!, tampered)).Should().BeFalse();
    }

    [Fact]
    public async Task Web_crypto_rejects_a_tampered_signature()
    {
        await using var context = await browser.NewContextAsync();
        var page = await OpenAsync(context);
        var signature = fixture.GetProperty("signature").GetString()!;
        var tampered = (signature[0] == 'A' ? 'B' : 'A') + signature[1..];

        (await VerifyAsync(page, tampered, fixture.GetProperty("canonicalText").GetString()!)).Should().BeFalse();
    }

    [Fact]
    public async Task A_private_or_non_p256_key_is_refused_by_the_verifier()
    {
        await using var context = await browser.NewContextAsync();
        var page = await OpenAsync(context);

        var accepted = await page.EvaluateAsync<bool[]>("""
            async () => {
              const module = await import('/js/invocation.js');
              const bytes = new TextEncoder().encode('rt');
              const cases = [
                { kty: 'EC', crv: 'P-384', x: 'a', y: 'b' },
                { kty: 'EC', crv: 'P-256', x: 'a', y: 'b', d: 'private' },
                { kty: 'oct', k: 'symmetric' },
              ];
              const results = [];
              for (const jwk of cases) {
                try { results.push(await module.verifySignature(JSON.stringify(jwk), 'AAAA', bytes)); }
                catch { results.push(false); }
              }
              return results;
            }
            """);

        accepted.Should().OnlyContain(value => value == false);
    }

    [Fact]
    public async Task An_unusable_link_shows_recovery_copy_and_clears_the_signed_query()
    {
        await using var context = await browser.NewContextAsync();
        context.SetDefaultTimeout(BootTimeoutMs);
        var page = await context.NewPageAsync();

        var payload = fixture.GetProperty("payloadUrl").GetString()!;
        var signature = fixture.GetProperty("signature").GetString()!;
        await page.GotoAsync($"{app.BaseUrl}/open?src=RouteTimer&v=1&payload={Uri.EscapeDataString(payload)}&name=Secret%20Route&ts=1787832000000&sig={signature}");

        await page.WaitForSelectorAsync("text=Could not import shared route", new() { Timeout = BootTimeoutMs });

        page.Url.Should().EndWith("/open");
        page.Url.Should().NotContain("sig=").And.NotContain("payload=").And.NotContain("Secret");
        (await page.ContentAsync()).Should().NotContain(signature);
    }

    [Fact]
    public async Task The_open_page_offers_manual_import_when_the_link_cannot_be_used()
    {
        await using var context = await browser.NewContextAsync();
        context.SetDefaultTimeout(BootTimeoutMs);
        var page = await context.NewPageAsync();
        await page.GotoAsync($"{app.BaseUrl}/open?src=Nope");
        await page.WaitForSelectorAsync("text=Could not import shared route", new() { Timeout = BootTimeoutMs });

        await page.Locator("input[type=file]").SetInputFilesAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "timed-route.gpx"));

        await page.WaitForSelectorAsync("a:has-text('Start ride')");
        (await page.Locator("a:has-text('Start ride')").GetAttributeAsync("href")).Should().StartWith("/track/");
    }
}
