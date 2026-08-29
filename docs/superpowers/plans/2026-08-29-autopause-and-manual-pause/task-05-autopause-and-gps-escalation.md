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

### Task 5: Autopause, and standing down the GPS on a long stop

Wires the preference into the ride and adds the battery escalation.

**Files:**
- Modify: `src/RoutePacer.App/Rides/RideSessionService.cs`
- Modify: `tests/RoutePacer.App.Tests/Rides/RideSessionServiceTests.cs`
- Modify: `tests/RoutePacer.App.Tests/Pages/TrackTests.cs` (constructor call only)

**Interfaces:**
- Consumes: `ISettingsRepository` and `AutoPauseSettings` from Task 3; `PauseMode` from Task 4.
- Produces: `RideSessionService(IRouteRepository routes, IRideRepository rides, ILocationService location, IWakeLockService wakeLock, ISettingsRepository settings, TimeProvider clock, RouteMatcher? matcher = null, PacingService? pacer = null)` — `settings` is inserted **fifth**, before `clock`.
- Produces: `public static readonly TimeSpan SuspendAfter = TimeSpan.FromMinutes(5)`.
- Produces: `public bool AutoPauseEnabled` getter, read by the tracking page in Task 7.

- [ ] **Step 1: Write the failing tests**

In `RideSessionServiceTests`, add the fake to the fields and thread it through `Create()`:

```csharp
    private readonly InMemorySettingsRepository settings = new();

    private RideSessionService Create() => new(routes, rides, location, wakeLock, settings, clock);
```

Then add:

```csharp
    [Fact]
    public async Task Standing_still_past_the_threshold_pauses_the_ride_when_autopause_is_on()
    {
        settings.AutoPause = new AutoPauseSettings(true, 20);
        var session = await Started();
        await location.PushAsync(Fix(0, 0));

        await location.PushAsync(Fix(25, 0.00005));

        session.State.Should().Be(RideSessionState.Paused);
        session.PauseMode.Should().Be(PauseMode.AutoStationary);
        location.Watching.Should().BeTrue();
    }

    [Fact]
    public async Task Standing_still_changes_nothing_when_autopause_is_off()
    {
        var session = await Started();
        await location.PushAsync(Fix(0, 0));

        await location.PushAsync(Fix(600, 0.00005));

        session.State.Should().Be(RideSessionState.Running);
    }

    [Fact]
    public async Task A_stop_shorter_than_the_threshold_does_not_pause()
    {
        settings.AutoPause = new AutoPauseSettings(true, 60);
        var session = await Started();
        await location.PushAsync(Fix(0, 0));

        await location.PushAsync(Fix(45, 0.00005));

        session.State.Should().Be(RideSessionState.Running);
    }

    [Fact]
    public async Task An_autopaused_ride_resumes_when_the_rider_sets_off()
    {
        settings.AutoPause = new AutoPauseSettings(true, 20);
        var session = await Started();
        await location.PushAsync(Fix(0, 0));
        await location.PushAsync(Fix(25, 0.00005));

        await location.PushAsync(Fix(60, 0.00015));

        session.State.Should().Be(RideSessionState.Running);
        session.PauseMode.Should().Be(PauseMode.None);
    }

    // Holding the watch and the wake lock through a cafe stop is the battery cost a movement-ending
    // pause would otherwise introduce. Five minutes is past any traffic light.
    [Fact]
    public async Task A_long_stop_gives_back_the_gps_watch_and_needs_a_tap_to_come_out_of()
    {
        var session = await Started();
        await location.PushAsync(Fix(0, 0));
        await session.PauseAsync();

        await location.PushAsync(Fix(310, 0.00005));

        session.PauseMode.Should().Be(PauseMode.Suspended);
        location.Watching.Should().BeFalse();
        wakeLock.ReleaseCount.Should().Be(1);
    }

    [Fact]
    public async Task Coming_out_of_a_long_stop_restarts_the_watch_and_forgets_the_stale_fix()
    {
        var session = await Started();
        await location.PushAsync(Fix(0, 0));
        await session.PauseAsync();
        await location.PushAsync(Fix(310, 0.00005));

        await session.ResumeAsync();

        location.StartCount.Should().Be(2);
        location.Watching.Should().BeTrue();
        session.State.Should().Be(RideSessionState.Running);

        // The first fix after the gap is far from the last one seen and must not be read as a spike.
        await location.PushAsync(Fix(320, 0.004));
        rides.Points.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Escalation_counts_from_when_the_rider_stopped_not_from_when_the_pause_began()
    {
        settings.AutoPause = new AutoPauseSettings(true, 20);
        var session = await Started();
        await location.PushAsync(Fix(0, 0));
        await location.PushAsync(Fix(25, 0.00005));
        session.PauseMode.Should().Be(PauseMode.AutoStationary);

        // Five minutes after the rider stopped, not five minutes after the pause was entered.
        await location.PushAsync(Fix(305, 0.00005));

        session.PauseMode.Should().Be(PauseMode.Suspended);
    }

    [Fact]
    public async Task Unreadable_settings_do_not_stop_a_ride_starting()
    {
        await routes.SaveAsync(track);
        var session = new RideSessionService(routes, rides, location, wakeLock, new ThrowingSettingsRepository(), clock);

        await session.StartAsync();

        session.State.Should().Be(RideSessionState.Running);
    }
```

Add the throwing fake to `tests/RoutePacer.App.Tests/Fakes.cs`:

```csharp
public sealed class ThrowingSettingsRepository : ISettingsRepository
{
    public Task<AutoPauseSettings> GetAutoPauseAsync(CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("storage unavailable");

    public Task SaveAutoPauseAsync(AutoPauseSettings settings, CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("storage unavailable");
}
```

In `tests/RoutePacer.App.Tests/Pages/TrackTests.cs`, add the field and thread it through:

```csharp
    private readonly InMemorySettingsRepository settings = new();
```

```csharp
        session = new RideSessionService(routes, rides, location, wakeLock, settings, clock);
        Services.AddSingleton<ISettingsRepository>(settings);
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test RoutePacer.slnx --filter "FullyQualifiedName~RideSessionServiceTests"`

Expected: FAIL to compile — the constructor takes no `ISettingsRepository`.

- [ ] **Step 3: Write the implementation**

In `src/RoutePacer.App/Rides/RideSessionService.cs`, replace **only** the `readonly` field
declarations and the constructor. The mutable state block below them — `route`, `ride`, `started`,
`pausedAt`, `pausedTotal`, `previousFix`, `totalDistance`, `previousSegment`, `sequence`,
`lastRouteDistance`, `statusMessage`, `lastAccuracy`, `wakeStatus`, `lastPublished`, and the
`pauseMode` field and `PauseMode` getter added in Task 4 — all stay exactly as they are.

```csharp
    /// <summary>Snapshots reach the UI at most this often. Every accepted fix is still persisted.</summary>
    public static readonly TimeSpan PublishInterval = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// How long a rider may stand still before the pause gives the GPS watch back. Holding it
    /// through a long stop is the battery cost of a pause that ends on movement; a fixed five
    /// minutes is past any traffic light and short of any real break.
    /// </summary>
    public static readonly TimeSpan SuspendAfter = TimeSpan.FromMinutes(5);

    private readonly IRouteRepository routes; private readonly IRideRepository rides;
    private readonly ILocationService location; private readonly IWakeLockService wakeLock;
    private readonly ISettingsRepository settings; private readonly TimeProvider clock;
    private readonly RouteMatcher matcher; private readonly PacingService pacer;
    private readonly GpsSpikeFilter filter = new();
    private readonly StationaryDetector stationary = new();

    private AutoPauseSettings autoPause = AutoPauseSettings.Default;

    public RideSessionService(IRouteRepository routes, IRideRepository rides, ILocationService location, IWakeLockService wakeLock, ISettingsRepository settings, TimeProvider clock, RouteMatcher? matcher = null, PacingService? pacer = null)
    {
        this.routes = routes; this.rides = rides; this.location = location; this.wakeLock = wakeLock;
        this.settings = settings; this.clock = clock;
        this.matcher = matcher ?? new RouteMatcher(); this.pacer = pacer ?? new PacingService();
        wakeLock.StatusChanged += OnWakeStatusChanged;
    }

    public bool AutoPauseEnabled => autoPause.Enabled;
```

After this step the class holds `stationary` declared once, `pauseMode` and its `PauseMode` getter
from Task 4, and `autoPause` new here. If the build reports `pauseMode` as undefined, the mutable
state block was overwritten — restore it rather than redeclaring the field here.

In `StartAsync`, immediately before the `ride = new RideSummary(...)` line:

```csharp
        // Read once. A preference that changed underneath a running ride would alter pacing with no
        // cause the rider could see. Unreadable storage is not a reason to refuse a ride.
        try { autoPause = await settings.GetAutoPauseAsync(); }
        catch { autoPause = AutoPauseSettings.Default; }
```

At the end of `OnFixAsync`, replace the bare `stationary.Observe(fix);` with:

```csharp
        var stillFor = stationary.Observe(fix);
        if (autoPause.Enabled && stillFor.TotalSeconds >= autoPause.ThresholdSeconds)
            await EnterWatchingPauseAsync(PauseMode.AutoStationary);
```

In `OnPausedFixAsync`, insert the escalation check between the resume check and the final publish:

```csharp
        if (stationary.StationaryTime(fix) >= SuspendAfter) { await SuspendAsync(); return; }
```

And add:

```csharp
    private async Task SuspendAsync()
    {
        await StopBrowserServicesAsync();
        pauseMode = PauseMode.Suspended;
        Publish(Snapshot?.Pacing, force: true);
    }
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test RoutePacer.slnx --filter "FullyQualifiedName~RideSessionServiceTests|FullyQualifiedName~TrackTests"`

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/RoutePacer.App/Rides/RideSessionService.cs tests/RoutePacer.App.Tests/Rides/RideSessionServiceTests.cs tests/RoutePacer.App.Tests/Pages/TrackTests.cs tests/RoutePacer.App.Tests/Fakes.cs
git commit -m "feat: pause by itself when the rider stops, and stand the GPS down on a long stop"
```

---

