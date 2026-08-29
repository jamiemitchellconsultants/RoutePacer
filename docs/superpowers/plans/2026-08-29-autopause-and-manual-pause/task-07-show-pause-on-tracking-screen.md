# Autopause and Manual Pause Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Freeze the ahead/behind reading while a rider is stopped — automatically after a chosen number of stationary seconds, or on a tap — and unfreeze it when they ride off.

**Architecture:** A pure `StationaryDetector` in `RoutePacer.Core.Tracking` answers "has the rider moved?" from position alone. `RideSessionService` delegates to it, gains a `PauseMode` alongside its existing `RideSessionState.Paused`, and keeps the GPS watch alive through a pause so movement can end it. `PacingService` learns to subtract paused time, which is the defect that made every pause cosmetic.

**Tech Stack:** .NET 10, Blazor WebAssembly, xUnit, FluentAssertions, bUnit (`BunitContext`), `Microsoft.Extensions.Time.Testing.FakeTimeProvider`, IndexedDB via a JS module.

**Spec:** `docs/superpowers/specs/2026-08-29-autopause-design.md`

## Global Constraints

- Solution file is `RoutePacer.slnx`. Build: `dotnet build RoutePacer.slnx`. Test: `dotnet test RoutePacer.slnx`.
- Scope a test run with `--filter`, e.g. `dotnet test RoutePacer.slnx --filter "FullyQualifiedName~StationaryDetectorTests"`. The `RoutePacer.E2E` project drives Playwright and is slow; do not run the unfiltered suite for a single task.
- Test method names are `Sentence_case_with_underscores` and read as claims about behaviour.
- No comments in code unless the *why* is non-obvious — a hidden constraint, a workaround, a surprising invariant. Do not narrate what the code plainly does.
- The tracker carries **no red or green**. Ahead and behind are told apart by where the word sits. Any new state must read as a word, a position, or a brightness — never a hue. See the comment block at the top of `wwwroot/css/tracker.css`.
- Autopause threshold: default **15 s**, minimum **5 s**, maximum **300 s**.
- Stationary radius **10 m**; resume radius **15 m**; escalation to GPS-off after **5 minutes** stationary.
- At the equator (where every test fixture sits) 0.0001° of longitude is 11.13 m. Test fixtures use latitude 0, so longitude offsets convert directly: `0.00005` ≈ 5.6 m, `0.00011` ≈ 12.2 m, `0.00015` ≈ 16.7 m.
- Commit after each task with a message whose subject says what changed for the rider, matching the repository's existing style.

---


---

### Task 7: Show the pause on the tracking screen

**Files:**
- Modify: `src/RoutePacer.App/Formatting/RideFormat.cs`
- Modify: `src/RoutePacer.App/Components/PaceDelta.razor`
- Modify: `src/RoutePacer.App/Pages/Track.razor`
- Modify: `src/RoutePacer.App/wwwroot/css/tracker.css`
- Test: `tests/RoutePacer.App.Tests/Pages/TrackTests.cs`
- Test: `tests/RoutePacer.App.Tests/Formatting/RideFormatTests.cs`

**Interfaces:**
- Consumes: `TrackerSnapshot.PauseMode`, `TrackerSnapshot.PausedFor`, `RideSessionService.AutoPauseEnabled`.
- Produces: `string? RideFormat.PauseDetail(PauseMode)`; `PaceDelta` gains `[Parameter] public bool Muted { get; set; }`.

A frozen number that looks identical to a live one is the trap here. The cue is brightness and a word, never a hue — the tracker carries no red or green by deliberate choice.

- [ ] **Step 1: Write the failing tests**

Add to `tests/RoutePacer.App.Tests/Formatting/RideFormatTests.cs`:

```csharp
    [Theory]
    [InlineData(PauseMode.AutoStationary, "stopped moving")]
    [InlineData(PauseMode.Manual, "Paused")]
    [InlineData(PauseMode.Suspended, "Resume")]
    public void A_paused_ride_says_why_and_how_to_leave_it(PauseMode mode, string expected)
        => RideFormat.PauseDetail(mode).Should().Contain(expected);

    [Fact]
    public void A_running_ride_has_no_pause_detail()
        => RideFormat.PauseDetail(PauseMode.None).Should().BeNull();
```

Add to `tests/RoutePacer.App.Tests/Pages/TrackTests.cs`:

```csharp
    private async Task<IRenderedComponent<TrackPage>> Riding()
    {
        await Seed();
        var page = Render<TrackPage>();
        page.Find("button").Click();
        page.FindAll("button").Single(b => b.TextContent.Contains("Start ride now")).Click();
        return page;
    }

    [Fact]
    public async Task A_pause_button_is_offered_when_autopause_is_off()
    {
        var page = await Riding();

        page.FindAll("button").Should().Contain(b => b.TextContent.Contains("Pause"));
    }

    [Fact]
    public async Task No_pause_button_is_offered_when_autopause_is_on()
    {
        settings.AutoPause = new AutoPauseSettings(true, 20);
        var page = await Riding();

        page.FindAll("button").Should().NotContain(b => b.TextContent.Trim() == "Pause");
    }

    [Fact]
    public async Task A_paused_tracker_says_it_is_paused_and_dims_the_frozen_reading()
    {
        var page = await Riding();

        page.FindAll("button").Single(b => b.TextContent.Trim() == "Pause").Click();

        page.Markup.Should().Contain("Paused");
        page.FindAll(".pace-delta-muted").Should().NotBeEmpty();
        page.FindAll("button").Should().Contain(b => b.TextContent.Contains("Resume"));
    }

    [Fact]
    public async Task A_suspended_tracker_offers_resume_even_when_autopause_is_on()
    {
        settings.AutoPause = new AutoPauseSettings(true, 20);
        var page = await Riding();
        await location.PushAsync(new GeoFix(Start, 0, 0, 5, null));
        await location.PushAsync(new GeoFix(Start.AddSeconds(25), 0, 0.00005, 5, null));
        await location.PushAsync(new GeoFix(Start.AddSeconds(310), 0, 0.00005, 5, null));

        page.Render();

        page.FindAll("button").Should().Contain(b => b.TextContent.Contains("Resume"));
    }

    // Stopping a ride must stay reachable in every pause mode.
    [Fact]
    public async Task Stop_ride_survives_a_pause()
    {
        var page = await Riding();

        page.FindAll("button").Single(b => b.TextContent.Trim() == "Pause").Click();

        page.FindAll("button").Should().Contain(b => b.TextContent.Contains("Stop ride"));
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test RoutePacer.slnx --filter "FullyQualifiedName~TrackTests|FullyQualifiedName~RideFormatTests"`

Expected: FAIL to compile — `RideFormat.PauseDetail` does not exist.

- [ ] **Step 3: Write the wording and the muted tile**

Add to `src/RoutePacer.App/Formatting/RideFormat.cs` (it needs `using RoutePacer.App.Rides;`):

```csharp
    /// <summary>
    /// Why the reading is frozen and what ends it. A word, not a hue: the tracker tells its states
    /// apart by what they say and where it sits, so that they survive sunlight and colour blindness.
    /// </summary>
    public static string? PauseDetail(PauseMode mode) => mode switch
    {
        PauseMode.AutoStationary => "Paused — you stopped moving. Ride on to resume.",
        PauseMode.Manual => "Paused. Ride on to resume.",
        PauseMode.Suspended => "Paused — GPS off to save battery. Tap Resume.",
        _ => null
    };
```

Replace `src/RoutePacer.App/Components/PaceDelta.razor`:

```razor
<div class="pace-delta pace-delta-@Tone @(Primary ? "pace-delta-primary" : null) @(Muted ? "pace-delta-muted" : null)">
    <span class="pace-delta-label">@Label</span>
    <strong class="pace-delta-value">@Value</strong>
    @if (!string.IsNullOrWhiteSpace(Detail)) { <span class="pace-delta-detail">@Detail</span> }
</div>
@code {
    [Parameter] public string Label { get; set; } = "";
    [Parameter] public string Value { get; set; } = "";
    [Parameter] public string? Detail { get; set; }
    [Parameter] public string Tone { get; set; } = "neutral";
    [Parameter] public bool Primary { get; set; }
    [Parameter] public bool Muted { get; set; }
}
```

Append to `src/RoutePacer.App/wwwroot/css/tracker.css`:

```css
/* A frozen reading must not look like a live one. Brightness carries that, because it survives
 * everything hue does not -- which is why the panels have no hue to spend here. */
.pace-delta-muted .pace-delta-value { opacity: .55; }
```

- [ ] **Step 4: Rework the tracking page**

In `src/RoutePacer.App/Pages/Track.razor`, replace the two `PaceDelta` tiles with versions that carry the pause:

```razor
        @if (s.RouteHasTiming)
        {
            <PaceDelta Primary="true" Label="Time" Value="@RideFormat.TimeDelta(s.Pacing?.DeltaTimeSeconds)"
                       Detail="@RideFormat.PauseDetail(s.PauseMode)" Muted="@Paused"
                       Tone="@RideFormat.TimeTone(s.Pacing?.DeltaTimeSeconds)" />
        }
        else
        {
            <PaceDelta Primary="true" Label="Time" Value="@RideFormat.TimingUnavailable"
                       Detail="@(RideFormat.PauseDetail(s.PauseMode) ?? "This route has no timing, so RoutePacer tracks distance only.")"
                       Muted="@Paused" Tone="neutral" />
        }
        <PaceDelta Label="Distance" Value="@RideFormat.Delta(s.Pacing?.DeltaDistanceMeters, "m")"
                   Muted="@Paused" Tone="@RideFormat.DistanceTone(s.Pacing?.DeltaDistanceMeters)" />
```

Add a row to the metrics list, after the `Elapsed` row:

```razor
            @if (Paused) { <dt>Paused</dt><dd>@RideFormat.Elapsed(s.PausedFor)</dd> }
```

Replace the single pause button with the mode-driven pair:

```razor
        @if (s.PauseMode is PauseMode.Manual or PauseMode.Suspended)
        {
            <button class="btn" @onclick="Resume" disabled="@(busy || Transitioning)">Resume</button>
        }
        else if (s.PauseMode == PauseMode.None && !Session.AutoPauseEnabled)
        {
            <button class="btn" @onclick="Pause" disabled="@(busy || Transitioning)">Pause</button>
        }
```

Replace `TogglePause` in the `@code` block:

```csharp
    private bool Paused => Session.Snapshot?.PauseMode is not (null or PauseMode.None);

    private Task Pause() => Run(Session.PauseAsync);

    private Task Resume() => Run(Session.ResumeAsync);
```

An `AutoStationary` pause offers no button on purpose: riding off is what ends it, and a Resume tap while still standing still would only be undone by the next fix.

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test RoutePacer.slnx --filter "FullyQualifiedName~TrackTests|FullyQualifiedName~RideFormatTests"`

Expected: PASS.

- [ ] **Step 6: Run the whole suite**

Run: `dotnet test RoutePacer.slnx`

Expected: PASS, every project. Investigate any E2E failure before committing — `TrackingCapabilityTests` exercises the tracking page against the published app.

- [ ] **Step 7: Commit**

```bash
git add src/RoutePacer.App/Formatting/RideFormat.cs src/RoutePacer.App/Components/PaceDelta.razor src/RoutePacer.App/Pages/Track.razor src/RoutePacer.App/wwwroot/css/tracker.css tests/RoutePacer.App.Tests/Pages/TrackTests.cs tests/RoutePacer.App.Tests/Formatting/RideFormatTests.cs
git commit -m "feat: show the rider that the ahead/behind is frozen, and what will unfreeze it"
```

---

## After the last task

- `Narrative.md` is generated and never hand-edited. The pull request needs the `narrative-required` label **and** the three body headings spelled exactly `## Narrative Context`, `## Narrative Decision`, `## Narrative Consequences`. The workflow fires on the merge event only, and neither a missing label nor missing sections can be repaired afterwards.
- Supplying a pull-request body replaces the repository template wholesale, so carry the three sections in the body yourself.
