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

