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

### Task 1: StationaryDetector

A pure class that decides whether a rider is standing still, from position and time alone. No GPS plumbing, no clock injection, no storage — every case is reachable by constructing `GeoFix` values directly.

**Files:**
- Create: `src/RoutePacer.Core/Tracking/StationaryDetector.cs`
- Test: `tests/RoutePacer.Core.Tests/Tracking/StationaryDetectorTests.cs`

**Interfaces:**
- Consumes: `RoutePacer.Core.Domain.GeoFix`, `RoutePacer.Core.Tracking.GeoMath.HaversineMeters`.
- Produces: `StationaryDetector` with `const double StationaryRadiusMeters = 10`, `const double ResumeRadiusMeters = 15`, `bool IsAnchored`, `void Reset()`, `TimeSpan Observe(GeoFix)`, `double MetersFromAnchor(GeoFix)`, `TimeSpan StationaryTime(GeoFix)`.

The split between `Observe` (re-anchors) and `MetersFromAnchor` / `StationaryTime` (do not) is what makes the paused path safe: a paused ride must measure displacement from where the rider stopped, and an `Observe` call would quietly move that origin as the rider shuffled about.

- [ ] **Step 1: Write the failing tests**

Create `tests/RoutePacer.Core.Tests/Tracking/StationaryDetectorTests.cs`:

```csharp
using FluentAssertions;
using RoutePacer.Core.Domain;
using RoutePacer.Core.Tracking;

namespace RoutePacer.Core.Tests.Tracking;

public sealed class StationaryDetectorTests
{
    private static readonly DateTimeOffset Start = new(2026, 8, 29, 12, 0, 0, TimeSpan.Zero);
    private readonly StationaryDetector _detector = new();

    private static GeoFix Fix(double seconds, double longitude)
        => new(Start.AddSeconds(seconds), 0, longitude, 5, null);

    [Fact]
    public void A_fresh_detector_is_not_anchored_and_reports_nothing()
    {
        _detector.IsAnchored.Should().BeFalse();
        _detector.MetersFromAnchor(Fix(0, 0)).Should().Be(0);
        _detector.StationaryTime(Fix(0, 0)).Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void The_first_fix_anchors_and_reports_no_time_yet()
    {
        _detector.Observe(Fix(0, 0)).Should().Be(TimeSpan.Zero);
        _detector.IsAnchored.Should().BeTrue();
    }

    [Fact]
    public void Time_accumulates_while_the_rider_stays_inside_the_stationary_radius()
    {
        _detector.Observe(Fix(0, 0));

        // 0.00005 deg is 5.6 m, inside the 10 m radius.
        _detector.Observe(Fix(30, 0.00005)).Should().Be(TimeSpan.FromSeconds(30));
        _detector.Observe(Fix(90, -0.00005)).Should().Be(TimeSpan.FromSeconds(90));
    }

    [Fact]
    public void Leaving_the_stationary_radius_re_anchors_and_restarts_the_clock()
    {
        _detector.Observe(Fix(0, 0));
        _detector.Observe(Fix(60, 0.00005)).Should().Be(TimeSpan.FromSeconds(60));

        // 0.00015 deg is 16.7 m, outside the 10 m radius.
        _detector.Observe(Fix(90, 0.00015)).Should().Be(TimeSpan.Zero);
        _detector.Observe(Fix(120, 0.00015)).Should().Be(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void Displacement_is_measured_from_the_anchor_without_moving_it()
    {
        _detector.Observe(Fix(0, 0));

        // 0.00011 deg is 12.2 m: past the stationary radius, short of the resume radius.
        _detector.MetersFromAnchor(Fix(30, 0.00011)).Should().BeApproximately(12.2, 0.3);
        _detector.MetersFromAnchor(Fix(60, 0.00015)).Should().BeApproximately(16.7, 0.3);

        // Neither reading disturbed the anchor, so time still runs from the original fix.
        _detector.StationaryTime(Fix(60, 0.00015)).Should().Be(TimeSpan.FromSeconds(60));
    }

    // The gap between the two radii is what stops a phone drifting on GPS noise from flapping
    // between paused and running, which a rider reads as the number flickering for no reason.
    [Fact]
    public void The_band_between_the_two_radii_is_neither_moving_nor_a_reason_to_re_anchor()
    {
        _detector.Observe(Fix(0, 0));
        var drifting = Fix(45, 0.00011);

        _detector.MetersFromAnchor(drifting).Should().BeGreaterThan(StationaryDetector.StationaryRadiusMeters);
        _detector.MetersFromAnchor(drifting).Should().BeLessThan(StationaryDetector.ResumeRadiusMeters);
        _detector.StationaryTime(drifting).Should().Be(TimeSpan.FromSeconds(45));
    }

    [Fact]
    public void Stationary_time_never_goes_negative_when_a_fix_arrives_out_of_order()
    {
        _detector.Observe(Fix(60, 0));

        _detector.StationaryTime(Fix(10, 0)).Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void Reset_forgets_the_anchor()
    {
        _detector.Observe(Fix(0, 0));

        _detector.Reset();

        _detector.IsAnchored.Should().BeFalse();
        _detector.Observe(Fix(30, 0)).Should().Be(TimeSpan.Zero);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test RoutePacer.slnx --filter "FullyQualifiedName~StationaryDetectorTests"`

Expected: FAIL to compile — `StationaryDetector` does not exist.

- [ ] **Step 3: Write the implementation**

Create `src/RoutePacer.Core/Tracking/StationaryDetector.cs`:

```csharp
using RoutePacer.Core.Domain;

namespace RoutePacer.Core.Tracking;

/// <summary>
/// Decides whether a rider is standing still, from position alone.
///
/// The two radii differ deliberately. One radius would let a phone drifting on GPS noise at the
/// boundary flap between paused and running, which a rider reads as the number flickering for no
/// reason they can see. Speed is not consulted: <see cref="GeoFix.SpeedMps"/> is optional, and
/// phones report it unreliably or not at all at exactly the speeds this has to tell apart.
/// </summary>
public sealed class StationaryDetector
{
    public const double StationaryRadiusMeters = 10;
    public const double ResumeRadiusMeters = 15;

    private double latitude, longitude;
    private DateTimeOffset anchoredAt;

    public bool IsAnchored { get; private set; }

    public void Reset() => IsAnchored = false;

    /// <summary>Time spent at the anchor, re-anchoring when the fix has left the stationary radius.</summary>
    public TimeSpan Observe(GeoFix fix)
    {
        if (IsAnchored && MetersFromAnchor(fix) <= StationaryRadiusMeters) return StationaryTime(fix);
        latitude = fix.Latitude;
        longitude = fix.Longitude;
        anchoredAt = fix.TimestampUtc;
        IsAnchored = true;
        return TimeSpan.Zero;
    }

    /// <summary>Displacement from the anchor, leaving the anchor where it is.</summary>
    public double MetersFromAnchor(GeoFix fix)
        => IsAnchored ? GeoMath.HaversineMeters(latitude, longitude, fix.Latitude, fix.Longitude) : 0;

    /// <summary>Time at the anchor as of this fix, leaving the anchor where it is.</summary>
    public TimeSpan StationaryTime(GeoFix fix)
    {
        if (!IsAnchored) return TimeSpan.Zero;
        var elapsed = fix.TimestampUtc - anchoredAt;
        return elapsed > TimeSpan.Zero ? elapsed : TimeSpan.Zero;
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test RoutePacer.slnx --filter "FullyQualifiedName~StationaryDetectorTests"`

Expected: PASS, 8 tests.

- [ ] **Step 5: Commit**

```bash
git add src/RoutePacer.Core/Tracking/StationaryDetector.cs tests/RoutePacer.Core.Tests/Tracking/StationaryDetectorTests.cs
git commit -m "feat: decide from position alone whether a rider is standing still"
```

---

### Task 2: Subtract paused time from the pacing delta

This is the defect from section 2 of the spec, and it stands on its own: after this task the pause button that already exists stops lying, before any new feature is built on top.

**Files:**
- Modify: `src/RoutePacer.Core/Tracking/PacingService.cs`
- Modify: `src/RoutePacer.App/Rides/RideSessionService.cs` (the `pacer.Calculate` call site, and `CurrentDuration`)
- Test: `tests/RoutePacer.Core.Tests/Tracking/PacingServiceTests.cs`
- Test: `tests/RoutePacer.App.Tests/Rides/RideSessionServiceTests.cs`

**Interfaces:**
- Produces: `PacingSnapshot PacingService.Calculate(RouteTrack route, MatchedPosition match, DateTimeOffset sessionStartedAtUtc, TimeSpan pausedTotal, GeoFix fix)` — `pausedTotal` is a **required** fourth parameter, inserted before `fix`.
- Produces: `private TimeSpan RideSessionService.PausedSoFar()`.

The parameter is required rather than defaulted. A default would silently preserve today's behaviour at the one call site where it is wrong, which is how this survived as long as it has.

- [ ] **Step 1: Write the failing tests**

In `tests/RoutePacer.Core.Tests/Tracking/PacingServiceTests.cs`, every existing `_pacer.Calculate(route, match, Start, Fix(...))` call gains `TimeSpan.Zero` before the fix argument. Then add:

```csharp
    [Fact]
    public void Paused_time_is_excluded_from_live_elapsed()
    {
        var route = RouteFixtures.Straight(metresPerSecond: 10);

        // 70 s of wall clock, 30 s of it paused, so 40 s of riding.
        var snapshot = _pacer.Calculate(route, new MatchedPosition(4, 500, 2, 0.5), Start, TimeSpan.FromSeconds(30), Fix(70));

        snapshot.LiveElapsed.Should().Be(TimeSpan.FromSeconds(40));
        snapshot.DeltaTimeSeconds.Should().BeApproximately(-10, 0.5);
    }

    // The defect this feature exists to correct: a stopped rider used to slide further behind for
    // the whole stop, because live elapsed came straight off the wall clock.
    [Fact]
    public void A_rider_who_stops_is_no_further_behind_when_they_set_off_again()
    {
        var route = RouteFixtures.Straight(metresPerSecond: 10);
        var match = new MatchedPosition(4, 500, 2, 0.5);

        var atTheStop = _pacer.Calculate(route, match, Start, TimeSpan.Zero, Fix(60));
        var fiveMinutesLater = _pacer.Calculate(route, match, Start, TimeSpan.FromMinutes(5), Fix(360));

        fiveMinutesLater.DeltaTimeSeconds.Should().BeApproximately(atTheStop.DeltaTimeSeconds!.Value, 0.5);
    }

    [Fact]
    public void Live_elapsed_never_goes_negative_when_paused_time_exceeds_the_wall_clock()
        => _pacer.Calculate(RouteFixtures.Straight(), new MatchedPosition(0, 0, 1, 0), Start, TimeSpan.FromMinutes(10), Fix(30))
            .LiveElapsed.Should().Be(TimeSpan.Zero);
```

In `tests/RoutePacer.App.Tests/Rides/RideSessionServiceTests.cs` add:

```csharp
    [Fact]
    public async Task A_pause_freezes_the_ahead_behind_reading_and_not_only_the_elapsed_clock()
    {
        var session = await Started();
        clock.Advance(TimeSpan.FromSeconds(60));
        await location.PushAsync(Fix(60, 0.005));
        var before = session.Snapshot!.Pacing!.DeltaTimeSeconds;

        await session.PauseAsync();
        clock.Advance(TimeSpan.FromMinutes(5));
        await session.ResumeAsync();
        await location.PushAsync(Fix(360, 0.005));

        session.Snapshot!.Pacing!.DeltaTimeSeconds.Should().BeApproximately(before!.Value, 1);
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test RoutePacer.slnx --filter "FullyQualifiedName~PacingServiceTests|FullyQualifiedName~RideSessionServiceTests"`

Expected: FAIL to compile — `Calculate` takes four arguments, not five.

- [ ] **Step 3: Write the implementation**

Replace the body of `src/RoutePacer.Core/Tracking/PacingService.cs`:

```csharp
using RoutePacer.Core.Domain;

namespace RoutePacer.Core.Tracking;

public sealed class PacingService
{
    public static double DeltaTime(double liveSeconds, double targetSeconds) => liveSeconds - targetSeconds;

    /// <summary>
    /// <paramref name="pausedTotal"/> is required rather than defaulted. Live elapsed drives every
    /// delta the rider reads, so a caller that forgets to subtract a pause must not compile.
    /// </summary>
    public PacingSnapshot Calculate(RouteTrack route, MatchedPosition match, DateTimeOffset sessionStartedAtUtc, TimeSpan pausedTotal, GeoFix fix)
    {
        var live = TimeSpan.FromSeconds(Math.Max(0, (fix.TimestampUtc - sessionStartedAtUtc - pausedTotal).TotalSeconds));
        if (!route.HasTiming) return new PacingSnapshot(fix.TimestampUtc, live, match, null, null, null, null, fix.SpeedMps);
        var target = TrackInterpolator.ElapsedAtDistance(route, match.RouteDistanceMeters);
        var expected = TrackInterpolator.DistanceAtElapsed(route, live.TotalSeconds);
        return new PacingSnapshot(fix.TimestampUtc, live, match, target, target.HasValue ? DeltaTime(live.TotalSeconds, target.Value) : null, expected, expected.HasValue ? match.RouteDistanceMeters - expected.Value : null, fix.SpeedMps);
    }
}
```

In `src/RoutePacer.App/Rides/RideSessionService.cs`, replace `CurrentDuration` with a pair so the displayed elapsed and the pacing delta cannot drift apart again:

```csharp
    private TimeSpan PausedSoFar() => pausedTotal + (pausedAt is { } at ? clock.GetUtcNow() - at : TimeSpan.Zero);

    private TimeSpan CurrentDuration()
        => TimeSpan.FromSeconds(Math.Max(0, (clock.GetUtcNow() - started - PausedSoFar()).TotalSeconds));
```

And in `OnFixAsync`, pass it:

```csharp
        var pacing = pacer.Calculate(route, match, started, PausedSoFar(), fix);
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test RoutePacer.slnx --filter "FullyQualifiedName~PacingServiceTests|FullyQualifiedName~RideSessionServiceTests"`

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/RoutePacer.Core/Tracking/PacingService.cs src/RoutePacer.App/Rides/RideSessionService.cs tests/RoutePacer.Core.Tests/Tracking/PacingServiceTests.cs tests/RoutePacer.App.Tests/Rides/RideSessionServiceTests.cs
git commit -m "fix: a paused rider no longer slides further behind while stopped"
```

---

### Task 3: Persist the autopause preference

A standing preference in its own IndexedDB store, independent of the route. Nothing consumes it yet; this task ends with it round-tripping.

**Files:**
- Create: `src/RoutePacer.Core/Domain/AutoPauseSettings.cs`
- Create: `src/RoutePacer.Core/Storage/ISettingsRepository.cs`
- Create: `src/RoutePacer.App/Storage/IndexedDbSettingsRepository.cs`
- Modify: `src/RoutePacer.App/wwwroot/js/storage.js`
- Modify: `src/RoutePacer.App/Program.cs`
- Modify: `tests/RoutePacer.App.Tests/Fakes.cs`
- Test: `tests/RoutePacer.App.Tests/Storage/IndexedDbRepositoryContractTests.cs`
- Test: `tests/RoutePacer.E2E/OfflinePwaTests.cs`

**Interfaces:**
- Produces: `AutoPauseSettings(bool Enabled, int ThresholdSeconds)` with `const int MinimumSeconds = 5`, `MaximumSeconds = 300`, `DefaultSeconds = 15`, `static AutoPauseSettings Default`, and `AutoPauseSettings Clamped()`.
- Produces: `ISettingsRepository` with `Task<AutoPauseSettings> GetAutoPauseAsync(CancellationToken = default)` and `Task SaveAutoPauseAsync(AutoPauseSettings, CancellationToken = default)`.
- Produces: `IndexedDbSettingsRepository(IIndexedDbModule db)` and its nested `record AutoPauseDto(bool Enabled, int ThresholdSeconds)`.
- Produces: `InMemorySettingsRepository` test fake with a settable `AutoPause` property and a `SaveCount`.
- Produces: JS `getAutoPause()` and `saveAutoPause(settings)`.

- [ ] **Step 1: Write the failing tests**

Add to `tests/RoutePacer.App.Tests/Storage/IndexedDbRepositoryContractTests.cs`:

```csharp
    [Fact]
    public async Task Autopause_defaults_to_off_when_nothing_is_stored()
    {
        var settings = await new IndexedDbSettingsRepository(new RecordingIndexedDbModule()).GetAutoPauseAsync();

        settings.Should().Be(AutoPauseSettings.Default);
        settings.Enabled.Should().BeFalse();
        settings.ThresholdSeconds.Should().Be(15);
    }

    [Fact]
    public async Task A_stored_autopause_preference_is_read_back()
    {
        var module = new RecordingIndexedDbModule();
        module.Results["getAutoPause"] = new IndexedDbSettingsRepository.AutoPauseDto(true, 45);

        var settings = await new IndexedDbSettingsRepository(module).GetAutoPauseAsync();

        settings.Should().Be(new AutoPauseSettings(true, 45));
    }

    // A hand-edited store must not produce a threshold that never fires or fires instantly.
    [Theory]
    [InlineData(0, 5)]
    [InlineData(4, 5)]
    [InlineData(9999, 300)]
    public async Task A_threshold_outside_the_accepted_range_is_clamped_on_read(int stored, int expected)
    {
        var module = new RecordingIndexedDbModule();
        module.Results["getAutoPause"] = new IndexedDbSettingsRepository.AutoPauseDto(true, stored);

        (await new IndexedDbSettingsRepository(module).GetAutoPauseAsync()).ThresholdSeconds.Should().Be(expected);
    }

    [Fact]
    public async Task Saving_autopause_clamps_before_it_reaches_storage()
    {
        var module = new RecordingIndexedDbModule();

        await new IndexedDbSettingsRepository(module).SaveAutoPauseAsync(new AutoPauseSettings(true, 1000));

        var call = module.Calls.Should().ContainSingle(c => c.Name == "saveAutoPause").Subject;
        call.Args.Should().ContainSingle().Which.Should().Be(new AutoPauseSettings(true, 300));
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test RoutePacer.slnx --filter "FullyQualifiedName~IndexedDbRepositoryContractTests"`

Expected: FAIL to compile — `AutoPauseSettings` and `IndexedDbSettingsRepository` do not exist.

- [ ] **Step 3: Write the domain record and the repository interface**

Create `src/RoutePacer.Core/Domain/AutoPauseSettings.cs`:

```csharp
namespace RoutePacer.Core.Domain;

/// <summary>
/// A standing rider preference, not a property of the route: it outlives any one import, so a rider
/// who always wants the same autopause sets it once.
/// </summary>
public sealed record AutoPauseSettings(bool Enabled, int ThresholdSeconds)
{
    public const int MinimumSeconds = 5;
    public const int MaximumSeconds = 300;
    public const int DefaultSeconds = 15;

    public static AutoPauseSettings Default { get; } = new(false, DefaultSeconds);

    public AutoPauseSettings Clamped()
        => this with { ThresholdSeconds = Math.Clamp(ThresholdSeconds, MinimumSeconds, MaximumSeconds) };
}
```

Create `src/RoutePacer.Core/Storage/ISettingsRepository.cs`:

```csharp
using RoutePacer.Core.Domain;

namespace RoutePacer.Core.Storage;

/// <summary>
/// Rider preferences that outlive the route and the ride. Separate from
/// <see cref="IRouteRepository"/> precisely so that importing a route does not reset them.
/// </summary>
public interface ISettingsRepository
{
    Task<AutoPauseSettings> GetAutoPauseAsync(CancellationToken cancellationToken = default);
    Task SaveAutoPauseAsync(AutoPauseSettings settings, CancellationToken cancellationToken = default);
}
```

Create `src/RoutePacer.App/Storage/IndexedDbSettingsRepository.cs`:

```csharp
using RoutePacer.Core.Domain;
using RoutePacer.Core.Storage;

namespace RoutePacer.App.Storage;

public sealed class IndexedDbSettingsRepository(IIndexedDbModule db) : ISettingsRepository
{
    public async Task<AutoPauseSettings> GetAutoPauseAsync(CancellationToken cancellationToken = default)
    {
        var dto = await db.InvokeAsync<AutoPauseDto>("getAutoPause").ConfigureAwait(false);
        return dto is null ? AutoPauseSettings.Default : new AutoPauseSettings(dto.Enabled, dto.ThresholdSeconds).Clamped();
    }

    public Task SaveAutoPauseAsync(AutoPauseSettings settings, CancellationToken cancellationToken = default)
        => db.InvokeVoidAsync("saveAutoPause", [settings.Clamped()]).AsTask();

    public sealed record AutoPauseDto(bool Enabled, int ThresholdSeconds);
}
```

- [ ] **Step 4: Add the settings store to the JS module**

In `src/RoutePacer.App/wwwroot/js/storage.js`, change the version constant and extend the comment above it:

```js
// Version 2 drops the ride history stores. RoutePacer is a pacing aide, not a recorder -- the rider
// already has something recording the ride -- so finished rides are no longer kept, and the upgrade
// deletes any that version 1 left behind rather than stranding them on the device forever.
// Version 3 adds rider preferences, which outlive both the route and the ride.
const databaseVersion = 3;
```

Inside `onupgradeneeded`, after the `active_ride_points` line:

```js
      if (!db.objectStoreNames.contains("settings")) db.createObjectStore("settings", { keyPath: "key" });
```

At the end of the file:

```js
// One row, so the key is a constant. Absent means the rider has never chosen, which the caller
// reads as the default rather than as an error.
export const getAutoPause = () => openDatabase().then(db => new Promise((resolve, reject) => {
  const tx = db.transaction(["settings"]);
  const row = tx.objectStore("settings").get("autoPause");
  tx.oncomplete = () => { db.close(); resolve(row.result ?? null); };
  tx.onerror = () => { db.close(); reject(transactionError(tx, "getAutoPause")); };
}));

export const saveAutoPause = settings => withTransaction(["settings"], "readwrite", tx =>
  tx.objectStore("settings").put({ key: "autoPause", enabled: settings.enabled, thresholdSeconds: settings.thresholdSeconds }));
```

- [ ] **Step 5: Register the repository and add the test fake**

In `src/RoutePacer.App/Program.cs`, after the `IRideRepository` registration:

```csharp
builder.Services.AddScoped<ISettingsRepository, IndexedDbSettingsRepository>();
```

In `tests/RoutePacer.App.Tests/Fakes.cs`, after `InMemoryRideRepository`:

```csharp
public sealed class InMemorySettingsRepository : ISettingsRepository
{
    public AutoPauseSettings AutoPause { get; set; } = AutoPauseSettings.Default;
    public int SaveCount { get; private set; }

    public Task<AutoPauseSettings> GetAutoPauseAsync(CancellationToken cancellationToken = default) => Task.FromResult(AutoPause);

    public Task SaveAutoPauseAsync(AutoPauseSettings settings, CancellationToken cancellationToken = default)
    {
        AutoPause = settings.Clamped(); SaveCount++;
        return Task.CompletedTask;
    }
}
```

- [ ] **Step 6: Update the E2E test that pins the store list**

`tests/RoutePacer.E2E/OfflinePwaTests.cs` asserts the object stores exactly, so the new store fails
it. In `The_imported_route_survives_a_reload_because_it_lives_in_indexeddb`, replace the assertion
and extend the comment:

```csharp
        // The version 2 upgrade drops the ride history stores. Their absence is the schema-level
        // statement that finished rides are not kept. Version 3 adds rider preferences, which
        // outlive both the route and the ride.
        stores.Should().BeEquivalentTo(["routes", "route_points", "active_ride", "active_ride_points", "settings"]);
```

- [ ] **Step 7: Prove the upgrade does not cost the rider their route**

A hand-built version 2 database is the only way to exercise the upgrade path against data rather
than against an empty one — a fresh browser profile creates version 3 outright and never runs it.
Add to `tests/RoutePacer.E2E/OfflinePwaTests.cs`:

```csharp
    [Fact]
    public async Task A_version_2_database_gains_the_settings_store_without_losing_its_route()
    {
        await using var context = await NewContextAsync();
        var page = await OpenAsync(context);

        await page.EvaluateAsync(SeedVersion2Database);

        // The import page reads the autopause preference when it opens, which is what makes the
        // application open the database and run the upgrade. It needs no valid route to render, so
        // the deliberately incomplete seeded row cannot fail the page before the upgrade happens.
        await page.GotoAsync($"{app.BaseUrl}/import");
        await page.WaitForSelectorAsync("input[type=file]");

        var state = await page.EvaluateAsync<UpgradedDatabase>(ReadDatabaseShape);

        state.Version.Should().Be(3);
        state.Names.Should().Contain("settings");
        state.Routes.Should().Be(1, "the upgrade is additive and must not cost the rider their route");
    }

    private sealed record UpgradedDatabase(int Version, string[] Names, int Routes);

    private const string SeedVersion2Database = @"
        async () => {
          await new Promise((resolve, reject) => {
            const request = indexedDB.deleteDatabase('routepacer');
            request.onsuccess = () => resolve();
            request.onerror = () => reject(request.error);
          });
          const db = await new Promise((resolve, reject) => {
            const request = indexedDB.open('routepacer', 2);
            request.onupgradeneeded = () => {
              const created = request.result;
              created.createObjectStore('routes', { keyPath: 'routeId' });
              created.createObjectStore('route_points', { keyPath: ['routeId', 'index'] });
              created.createObjectStore('active_ride', { keyPath: 'rideId' });
              created.createObjectStore('active_ride_points', { keyPath: ['rideId', 'sequence'] });
            };
            request.onsuccess = () => resolve(request.result);
            request.onerror = () => reject(request.error);
          });
          await new Promise((resolve, reject) => {
            const tx = db.transaction(['routes'], 'readwrite');
            tx.objectStore('routes').put({ routeId: 'seeded', name: 'Seeded route' });
            tx.oncomplete = () => resolve();
            tx.onerror = () => reject(tx.error);
          });
          db.close();
        }";

    private const string ReadDatabaseShape = @"
        async () => {
          const db = await new Promise((resolve, reject) => {
            const request = indexedDB.open('routepacer');
            request.onsuccess = () => resolve(request.result);
            request.onerror = () => reject(request.error);
          });
          const version = db.version;
          const names = Array.from(db.objectStoreNames);
          const routes = await new Promise((resolve, reject) => {
            const request = db.transaction(['routes']).objectStore('routes').getAll();
            request.onsuccess = () => resolve(request.result.length);
            request.onerror = () => reject(request.error);
          });
          db.close();
          return { version, names, routes };
        }";
```

- [ ] **Step 8: Run the tests to verify they pass**

Run: `dotnet test RoutePacer.slnx --filter "FullyQualifiedName~IndexedDbRepositoryContractTests"`

Expected: PASS, including the three clamping theory cases.

Then the E2E pair, which builds and publishes the app and is slow:

Run: `dotnet test RoutePacer.slnx --filter "FullyQualifiedName~OfflinePwaTests"`

Expected: PASS.

- [ ] **Step 9: Commit**

```bash
git add src/RoutePacer.Core/Domain/AutoPauseSettings.cs src/RoutePacer.Core/Storage/ISettingsRepository.cs src/RoutePacer.App/Storage/IndexedDbSettingsRepository.cs src/RoutePacer.App/wwwroot/js/storage.js src/RoutePacer.App/Program.cs tests/RoutePacer.App.Tests/Fakes.cs tests/RoutePacer.App.Tests/Storage/IndexedDbRepositoryContractTests.cs tests/RoutePacer.E2E/OfflinePwaTests.cs
git commit -m "feat: remember the rider's autopause preference across routes"
```

---

### Task 4: A pause that ends when the rider moves

Turns the existing pause into one that keeps the GPS watch alive and lets movement end it. Autopause is not wired up yet — this task is the manual pause working end to end at the service level.

**Files:**
- Create: `src/RoutePacer.App/Rides/PauseMode.cs`
- Modify: `src/RoutePacer.App/Rides/TrackerSnapshot.cs`
- Modify: `src/RoutePacer.App/Rides/RideSessionService.cs`
- Test: `tests/RoutePacer.App.Tests/Rides/RideSessionServiceTests.cs`

**Interfaces:**
- Consumes: `StationaryDetector` from Task 1; `PausedSoFar()` from Task 2.
- Produces: `enum PauseMode { None, AutoStationary, Manual, Suspended }`.
- Produces: `TrackerSnapshot` gains trailing `PauseMode PauseMode = PauseMode.None` and `TimeSpan PausedFor = default`, both after the existing `string? Error = null`, so every existing positional construction still compiles.
- Produces: `RideSessionService.PauseMode` public getter; `private Task EnterWatchingPauseAsync(PauseMode)`, `private void ClosePause()`, `private Task ResumeOnMovementAsync(GeoFix)`, `private Task OnPausedFixAsync(GeoFix)`.

`RideSessionState` is deliberately untouched. `Paused` stays a single state so `Active`, recovery, and every existing branch keep working; the *kind* of pause rides alongside it.

- [ ] **Step 1: Write the failing tests**

Replace the existing `Pause_stops_the_watch_and_resume_restarts_it` test — a watching pause no longer stops the watch — and add the rest:

```csharp
    [Fact]
    public async Task A_manual_pause_keeps_the_gps_watch_up_because_movement_is_what_ends_it()
    {
        var session = await Started();

        await session.PauseAsync();

        session.State.Should().Be(RideSessionState.Paused);
        session.PauseMode.Should().Be(PauseMode.Manual);
        location.Watching.Should().BeTrue();
        wakeLock.ReleaseCount.Should().Be(0);
    }

    [Fact]
    public async Task Riding_off_ends_a_manual_pause_without_a_tap()
    {
        var session = await Started();
        await location.PushAsync(Fix(10, 0));
        await session.PauseAsync();

        // 0.00015 deg is 16.7 m, past the 15 m resume radius.
        await location.PushAsync(Fix(70, 0.00015));

        session.State.Should().Be(RideSessionState.Running);
        session.PauseMode.Should().Be(PauseMode.None);
    }

    [Fact]
    public async Task Drifting_inside_the_resume_radius_does_not_end_a_pause()
    {
        var session = await Started();
        await location.PushAsync(Fix(10, 0));
        await session.PauseAsync();

        // 0.00011 deg is 12.2 m: past the stationary radius, short of the resume radius.
        await location.PushAsync(Fix(40, 0.00011));
        await location.PushAsync(Fix(70, -0.00011));

        session.State.Should().Be(RideSessionState.Paused);
    }

    [Fact]
    public async Task A_paused_ride_records_no_points_and_accumulates_no_distance()
    {
        var session = await Started();
        await location.PushAsync(Fix(10, 0));
        var pointsAtPause = rides.Points.Count;
        var distanceAtPause = session.Snapshot!.DistanceMeters;
        await session.PauseAsync();

        for (var i = 1; i <= 5; i++) await location.PushAsync(Fix(10 + i * 10, i % 2 == 0 ? 0.00005 : -0.00005));

        rides.Points.Should().HaveCount(pointsAtPause);
        session.Snapshot!.DistanceMeters.Should().Be(distanceAtPause);
    }

    [Fact]
    public async Task Tapping_resume_ends_a_manual_pause_without_restarting_a_watch_that_never_stopped()
    {
        var session = await Started();
        await session.PauseAsync();

        await session.ResumeAsync();

        session.State.Should().Be(RideSessionState.Running);
        session.PauseMode.Should().Be(PauseMode.None);
        location.StartCount.Should().Be(1);
    }

    [Fact]
    public async Task The_snapshot_carries_the_pause_kind_and_how_long_it_has_run()
    {
        var session = await Started();
        await location.PushAsync(Fix(10, 0));
        await session.PauseAsync();

        clock.Advance(TimeSpan.FromSeconds(20));
        await location.PushAsync(Fix(30, 0.00005));

        session.Snapshot!.PauseMode.Should().Be(PauseMode.Manual);
        session.Snapshot!.PausedFor.Should().BeCloseTo(TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task A_recovered_ride_comes_back_suspended_with_no_watch()
    {
        await routes.SaveAsync(track);
        rides.SeedActive(new RideSummary(Guid.NewGuid(), track.Summary.RouteId, Start, null, RideStatus.Running, 500, 300, 0));
        var session = Create();

        await session.RestoreActiveRideAsync();

        session.State.Should().Be(RideSessionState.Paused);
        session.PauseMode.Should().Be(PauseMode.Suspended);
        location.Watching.Should().BeFalse();
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test RoutePacer.slnx --filter "FullyQualifiedName~RideSessionServiceTests"`

Expected: FAIL to compile — `PauseMode` does not exist.

- [ ] **Step 3: Add the pause mode and widen the snapshot**

Create `src/RoutePacer.App/Rides/PauseMode.cs`:

```csharp
namespace RoutePacer.App.Rides;

/// <summary>
/// What kind of pause a <see cref="RideSessionState.Paused"/> ride is in. The first three keep the
/// GPS watch up so movement can end the pause; <see cref="Suspended"/> has given the watch back to
/// save battery, and only a tap brings it out.
/// </summary>
public enum PauseMode { None, AutoStationary, Manual, Suspended }
```

Replace `src/RoutePacer.App/Rides/TrackerSnapshot.cs`:

```csharp
using RoutePacer.App.Browser;
using RoutePacer.Core.Domain;

namespace RoutePacer.App.Rides;

public sealed record TrackerSnapshot(
    RideSessionState State,
    RouteSummary Route,
    PacingSnapshot? Pacing,
    double DistanceMeters,
    TimeSpan Elapsed,
    bool RouteHasTiming,
    long SavedPointCount,
    double? AccuracyMeters,
    WakeLockStatus WakeStatus,
    string? Error = null,
    PauseMode PauseMode = PauseMode.None,
    TimeSpan PausedFor = default);
```

- [ ] **Step 4: Rework the pause paths in the session service**

In `src/RoutePacer.App/Rides/RideSessionService.cs` add two fields, each in its own existing group.
Placement matters: Task 5 replaces the `readonly` group wholesale, so a `stationary` that has drifted
into the mutable block would end up declared twice.

Beside `private readonly GpsSpikeFilter filter = new();`, in the `readonly` group:

```csharp
    private readonly StationaryDetector stationary = new();
```

Beside `private DateTimeOffset started; private DateTimeOffset? pausedAt; private TimeSpan pausedTotal;`, in the mutable group:

```csharp
    private PauseMode pauseMode = PauseMode.None;
```

And beside the existing `public TrackerSnapshot? Snapshot { get; private set; }`:

```csharp
    public PauseMode PauseMode => pauseMode;
```

In `StartAsync`, alongside the other resets on the line beginning `previousFix = null;`:

```csharp
        stationary.Reset(); pauseMode = PauseMode.None;
```

Replace `PauseAsync` and `ResumeAsync`:

```csharp
    public async Task PauseAsync()
    {
        if (State != RideSessionState.Running) throw new InvalidOperationException("Ride is not running.");
        await EnterWatchingPauseAsync(PauseMode.Manual);
    }

    /// <summary>
    /// Pauses without giving up the GPS watch. Movement is what ends this pause, and a released
    /// watch could not see it.
    /// </summary>
    private async Task EnterWatchingPauseAsync(PauseMode mode)
    {
        pausedAt = clock.GetUtcNow(); pauseMode = mode; State = RideSessionState.Paused;
        if (ride is not null)
        {
            ride = ride with { Status = RideStatus.Paused, DurationSeconds = CurrentDuration().TotalSeconds, TotalDistanceMeters = totalDistance };
            await rides.SaveAsync(ride);
        }
        Publish(Snapshot?.Pacing, force: true);
    }

    public async Task ResumeAsync()
    {
        if (State != RideSessionState.Paused) throw new InvalidOperationException("Ride is not paused.");
        if (pauseMode == PauseMode.Suspended)
        {
            // The watch was given back, so the first fix afterwards is arbitrarily far from the last
            // one seen and would be rejected as a spike; the segment hint is stale for the same reason.
            filter.Reset(); previousFix = null; previousSegment = null; stationary.Reset();
            try { await wakeLock.AcquireAsync(); } catch { wakeStatus = WakeLockStatus.Failed; }
            await location.StartAsync(OnFixAsync, OnLocationErrorAsync);
        }
        ClosePause();
        if (ride is not null) { ride = ride with { Status = RideStatus.Running }; await rides.SaveAsync(ride); }
        Publish(Snapshot?.Pacing, force: true);
    }

    private void ClosePause()
    {
        if (pausedAt is { } pause) pausedTotal += clock.GetUtcNow() - pause;
        pausedAt = null; pauseMode = PauseMode.None; State = RideSessionState.Running;
    }

    private async Task ResumeOnMovementAsync(GeoFix fix)
    {
        ClosePause();
        // The pause interval went unmeasured, so the movement that ended it is not counted as ridden
        // distance -- the position recovery already takes across a gap it did not watch.
        previousFix = fix;
        stationary.Reset(); stationary.Observe(fix);
        if (ride is not null) { ride = ride with { Status = RideStatus.Running }; await rides.SaveAsync(ride); }
        Publish(Snapshot?.Pacing, force: true);
    }
```

Replace the head of `OnFixAsync` and add the paused branch:

```csharp
    private async Task OnFixAsync(GeoFix fix)
    {
        if (route is null || ride is null) return;
        if (State == RideSessionState.Paused) { await OnPausedFixAsync(fix); return; }
        if (State != RideSessionState.Running || !filter.Accept(fix)) return;
        lastAccuracy = fix.AccuracyMeters;
        var match = matcher.Match(route, fix, previousSegment);
        if (match is null) { statusMessage = "Off route — waiting to rejoin."; Publish(Snapshot?.Pacing); return; }
        statusMessage = null;
        previousSegment = match.SegmentIndex; lastRouteDistance = match.RouteDistanceMeters;
        if (previousFix is not null) totalDistance += GeoMath.HaversineMeters(previousFix.Latitude, previousFix.Longitude, fix.Latitude, fix.Longitude);
        previousFix = fix;
        var pacing = pacer.Calculate(route, match, started, PausedSoFar(), fix);
        var point = new RidePoint(ride.RideId, sequence++, fix.TimestampUtc, fix.Latitude, fix.Longitude, fix.SpeedMps, fix.AccuracyMeters, match.RouteDistanceMeters, pacing.DeltaDistanceMeters, pacing.DeltaTimeSeconds, match.CrossTrackErrorMeters);
        await rides.AppendPointAsync(point);
        Publish(pacing);

        // Observed on every running fix whether or not autopause is on: a manual pause needs an
        // anchor to measure the rider's departure from.
        stationary.Observe(fix);
    }

    /// <summary>
    /// A paused ride watches for departure and nothing else. No point is appended and no distance
    /// accrues: an hour parked would otherwise add phantom metres of GPS jitter, and the distance
    /// delta would be wrong the moment the rider set off again.
    /// </summary>
    private async Task OnPausedFixAsync(GeoFix fix)
    {
        if (pauseMode == PauseMode.Suspended || !filter.Accept(fix)) return;
        lastAccuracy = fix.AccuracyMeters;
        if (!stationary.IsAnchored) { stationary.Observe(fix); Publish(Snapshot?.Pacing); return; }
        if (stationary.MetersFromAnchor(fix) > StationaryDetector.ResumeRadiusMeters) { await ResumeOnMovementAsync(fix); return; }
        Publish(Snapshot?.Pacing);
    }
```

In `RestoreActiveRideAsync`, beside the existing `filter.Reset();`:

```csharp
        stationary.Reset(); pauseMode = PauseMode.Suspended;
```

In `Publish`, widen the snapshot construction:

```csharp
        Snapshot = new TrackerSnapshot(State, route.Summary, pacing, pacing?.Match.RouteDistanceMeters ?? lastRouteDistance,
            CurrentDuration(), route.HasTiming, sequence, lastAccuracy, wakeStatus, statusMessage, pauseMode,
            pausedAt is { } pausedSince ? clock.GetUtcNow() - pausedSince : TimeSpan.Zero);
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test RoutePacer.slnx --filter "FullyQualifiedName~RideSessionServiceTests"`

Expected: PASS. The existing `Paused_time_is_excluded_from_the_recorded_duration` test must still pass unchanged.

- [ ] **Step 6: Commit**

```bash
git add src/RoutePacer.App/Rides/PauseMode.cs src/RoutePacer.App/Rides/TrackerSnapshot.cs src/RoutePacer.App/Rides/RideSessionService.cs tests/RoutePacer.App.Tests/Rides/RideSessionServiceTests.cs
git commit -m "feat: a pause the rider ends by riding off, not by tapping"
```

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

### Task 6: Choose autopause when loading a route

**Files:**
- Modify: `src/RoutePacer.App/Pages/ImportRoute.razor`
- Test: `tests/RoutePacer.App.Tests/Pages/ImportRouteTests.cs`

**Interfaces:**
- Consumes: `ISettingsRepository`, `AutoPauseSettings` from Task 3.

The controls write on change rather than on import, so the choice does not depend on the file parsing and survives a failed import.

- [ ] **Step 1: Write the failing tests**

In `ImportRouteTests`, register the fake in the constructor:

```csharp
    private readonly InMemorySettingsRepository settings = new();
```

```csharp
        Services.AddSingleton<ISettingsRepository>(settings);
```

Add:

```csharp
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
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test RoutePacer.slnx --filter "FullyQualifiedName~ImportRouteTests"`

Expected: FAIL — no checkbox or number input on the page.

- [ ] **Step 3: Write the implementation**

In `src/RoutePacer.App/Pages/ImportRoute.razor`, add the injection at the top:

```razor
@inject ISettingsRepository Settings
```

Add the fieldset after the `<InputFile ... />` line:

```razor
<fieldset class="autopause">
    <legend>Autopause</legend>
    <label>
        <input type="checkbox" checked="@autoPause.Enabled" @onchange="ToggleAutoPause" />
        Pause the ahead/behind when I stop
    </label>
    <label>
        After
        <input type="number" min="@AutoPauseSettings.MinimumSeconds" max="@AutoPauseSettings.MaximumSeconds" step="1"
               value="@autoPause.ThresholdSeconds" disabled="@(!autoPause.Enabled)" @onchange="ChangeThreshold" />
        seconds standing still
    </label>
</fieldset>
```

Extend the `@code` block:

```csharp
    private AutoPauseSettings autoPause = AutoPauseSettings.Default;

    protected override async Task OnInitializedAsync() => autoPause = await Settings.GetAutoPauseAsync();

    private Task ToggleAutoPause(ChangeEventArgs args) => Save(autoPause with { Enabled = args.Value is true });

    private Task ChangeThreshold(ChangeEventArgs args)
        => int.TryParse(args.Value?.ToString(), out var seconds)
            ? Save(autoPause with { ThresholdSeconds = seconds })
            : Task.CompletedTask;

    // Written on change rather than on import: the preference outlives the route, and a file that
    // failed to parse is no reason to lose the rider's choice.
    private async Task Save(AutoPauseSettings updated)
    {
        autoPause = updated.Clamped();
        await Settings.SaveAutoPauseAsync(autoPause);
    }
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test RoutePacer.slnx --filter "FullyQualifiedName~ImportRouteTests"`

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/RoutePacer.App/Pages/ImportRoute.razor tests/RoutePacer.App.Tests/Pages/ImportRouteTests.cs
git commit -m "feat: choose autopause and its threshold when loading a route"
```

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
