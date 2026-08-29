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

