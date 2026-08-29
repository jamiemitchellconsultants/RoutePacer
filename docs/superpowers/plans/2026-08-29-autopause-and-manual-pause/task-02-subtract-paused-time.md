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

