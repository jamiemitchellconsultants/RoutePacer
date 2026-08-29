# Autopause and Manual Pause Design

**Date:** 2026-08-29

**Status:** Approved in chat

## 1. Goal

Stop a rider being punished for standing still.

Two related controls:

1. **Autopause.** On the import screen the rider enables autopause and chooses a stationary time in
   seconds. During a ride, staying put for longer than that freezes the ahead/behind reading until
   they ride off again.
2. **Manual pause.** With autopause off, the tracking screen offers a pause button. Tapping it
   freezes the ahead/behind until the rider starts moving.

Both share one mechanism: a pause that ends by itself when the rider moves.

## 2. The defect this uncovers

`PacingService.Calculate` derives live elapsed straight from the wall clock:

    live = fix.TimestampUtc - sessionStartedAtUtc

`RideSessionService` already tracks `pausedTotal`, but subtracts it only inside `CurrentDuration()`,
which feeds the displayed *Elapsed* and the stored ride duration. The pacing delta never sees it.

So today's pause button pauses the clock the rider reads and not the number they ride by. A rider who
pauses at a cafe watches themselves slide further behind for the whole stop. Neither feature in this
design means anything until that is fixed, so the fix is in scope, not adjacent to it.

The correction changes behaviour that exists today: after any pause the delta will read differently
than it does now. That is the point, but it is a visible change and not merely an internal one.

## 3. Selected Approach

Extract a pure `StationaryDetector` into `RoutePacer.Core.Tracking` and have `RideSessionService`
delegate to it.

This follows the split the codebase already uses: pure algorithms in Core (`RouteMatcher`,
`PacingService`, `GeoMath`, `TrackInterpolator`) with orchestration in the App layer. The detector
needs no GPS plumbing, no browser, and no clock injection to be tested exhaustively.

### 3.1 Alternatives not selected

**Folding detection and escalation into `RideSessionService`** adds no new types, but that class
already runs to 217 lines coordinating GPS, matching, pacing, persistence, wake lock and publishing.
Movement detection and an escalation timer would push it further, and every detector test would then
need location and storage fakes to reach the logic.

**Driving pause from `GpsSpikeFilter`** was rejected because the filter answers a different question
-- whether a fix is credible -- and pause policy has no business changing what counts as a good fix.

**Detecting movement from `GeoFix.SpeedMps`** was rejected during brainstorming. The field is
nullable, and phones report it unreliably or not at all at walking pace, which is exactly the
condition autopause must detect.

## 4. Detection

`StationaryDetector` keeps a single anchor: a position, and the time the rider reached it.

- `Observe(fix)` returns how long the rider has been stationary. If the fix lies more than
  **10 m** from the anchor, the detector re-anchors on that fix and returns zero. Otherwise it
  returns `fix.TimestampUtc - anchorAt`.
- `MetersFromAnchor(fix)` returns displacement from the anchor, for the resume test at **15 m**.
- `Reset()` forgets the anchor, for use where the fix stream has a gap.

Distances use the existing `GeoMath.HaversineMeters`.

The 10 m / 15 m gap is deliberate hysteresis. A single radius would let a phone drifting on GPS noise
at the boundary flap between paused and running, which on the tracking screen would read as the
number flickering for no reason the rider can see.

`GpsSpikeFilter` keeps running while paused, so an implausible fix cannot fake a resume.

## 5. Settings

`AutoPauseSettings(bool Enabled, int ThresholdSeconds)` in `RoutePacer.Core.Domain`.

`ISettingsRepository` joins `IRouteRepository` and `IRideRepository` in `RoutePacer.Core.Storage`,
implemented by `IndexedDbSettingsRepository` in the App layer.

The setting is a **standing preference**, not a property of the route. It outlives any one import, so
a rider who always wants a 20 second autopause sets it once. It is therefore not part of the atomic
route replace, and re-importing a GPX does not reset it.

`storage.js` moves to **database version 3**, adding a single-row `settings` object store. The
existing upgrade block is additive and guards every store with `objectStoreNames.contains`, so a
version 2 database gains the store without disturbing routes or an in-progress ride.

Default **15 seconds**; accepted range **5 to 300 seconds**. Values outside the range are rejected by
the input and clamped on read, so a hand-edited store cannot produce a threshold that never fires.

The session reads the preference once, in `StartAsync`. Changing it mid-ride is not possible without
leaving the tracking screen, and a setting that changed underneath a running ride would alter pacing
behaviour with no visible cause.

## 6. Session state machine

`RideSessionState` is unchanged. `TrackerSnapshot` gains a `PauseMode`:

    PauseMode { None, AutoStationary, Manual, Suspended }

Keeping `Paused` as a single `RideSessionState` means `Active`, recovery, and every existing branch
in the service and the tests continue to hold.

| From | Trigger | To | GPS and wake lock |
|---|---|---|---|
| Running | stationary >= threshold, autopause enabled | AutoStationary | stay on |
| Running | rider taps Pause, autopause disabled | Manual | stay on |
| AutoStationary or Manual | moves more than 15 m from anchor | Running | unchanged |
| AutoStationary or Manual | stationary >= 5 minutes | Suspended | released |
| Suspended | rider taps Resume | Running | reacquired |
| any active state | recovery after crash or reload | Suspended | off |

**Escalation** is measured from the detector's anchor, so it is five minutes stationary in total, not
five minutes on top of the autopause threshold. Its purpose is battery: holding the GPS watch and the
wake lock through a long cafe stop is the regression that keeping GPS alive would otherwise
introduce, and five minutes is long enough that no traffic light reaches it.

Escalation is evaluated when a fix arrives. If the GPS stream falls silent no escalation occurs,
which is acceptable: a silent watch is not the battery cost the escalation exists to avoid.

**Recovery** maps onto `Suspended` exactly, preserving today's rule that a recovered ride comes back
paused with GPS off and never requests location permission before the rider asks.

### 6.1 Fix handling while paused

Per the decision taken in brainstorming, a paused ride runs the displacement check only. It does not
match the route, does not append a `RidePoint`, and does not accumulate `totalDistance`. An hour
parked would otherwise add phantom metres from GPS jitter and corrupt the distance delta the moment
the rider resumed. The recorded track shows a clean gap for the stop, which is what a pause means.

Snapshots are still published while paused, so the screen can show that the ride is paused and for
how long.

### 6.2 Resuming

Resuming from `AutoStationary` or `Manual` re-anchors the detector on the resuming fix and sets
`previousFix` to it **without** accumulating the gap. The pause interval was not measured, and the
service already takes this position for a recovered ride rather than inventing distance across an
unmeasured gap.

Resuming from `Suspended` additionally calls `filter.Reset()` and clears `previousSegment` and
`previousFix`, exactly as recovery does. GPS was off, so the first fix afterwards is arbitrarily far
from the last one seen and would otherwise be rejected as a spike.

## 7. Pacing correction

`PacingService.Calculate` takes a `TimeSpan pausedTotal`:

    live = max(0, fix.TimestampUtc - sessionStartedAtUtc - pausedTotal)

The parameter is required rather than defaulted. A default would silently preserve the present
behaviour at the one call site where it is wrong, which is how the defect survived this long.

`RideSessionService` gains a `PausedSoFar()` helper returning
`pausedTotal + (pausedAt is {} at ? now - at : Zero)`; `CurrentDuration()` is refactored to use it so
the displayed elapsed and the pacing delta cannot drift apart again.

Because `pausedTotal` grows in real time while paused, the frozen delta stays arithmetically correct
rather than merely stale, and resuming continues from the right place.

Call sites: `RideSessionService` and `PacingServiceTests`.

## 8. User interface

### 8.1 Import screen

A checkbox for autopause and a number input for the threshold in seconds, the input disabled while
the checkbox is clear. Both write to the preference store on change rather than on import, so the
choice does not depend on the file parsing and survives a failed import.

The current stored values populate the controls on load.

### 8.2 Tracking screen

The pause/resume button follows the mode. The Stop ride button and its confirmation are
unaffected and remain available in every mode.

| Mode | Button |
|---|---|
| Running, autopause disabled | Pause |
| Running, autopause enabled | none |
| AutoStationary | none -- riding off is the exit |
| Manual | Resume -- for a pause tapped by mistake |
| Suspended | Resume |

`Suspended` offers Resume whether or not autopause is enabled, because it is the only way back.

The tracker carries no red or green by deliberate choice: `RideFormat` argues that word placement
survives direct sunlight and the common colour-vision deficiencies where colour does not. Paused
state therefore reads as a word, not a tint. The delta tiles keep their frozen reading and gain a
"Paused" line; the reason and the duration of the pause appear in the metrics list beneath.

## 9. Testing

Test-driven, matching the repository's existing practice.

**New**

- `StationaryDetectorTests` -- threshold reached, threshold not reached, re-anchoring on movement,
  hysteresis in both directions, no flapping across the 10 to 15 m band, escalation timing.
- Settings cases in the existing `IndexedDbRepositoryContractTests`, including the version 2 to 3
  upgrade leaving routes and an active ride intact.

**Updated**

- `PacingServiceTests` -- paused time excluded from live elapsed, plus a regression test that a rider
  paused for five minutes is no further behind on resuming than when they stopped. This is the
  defect in section 2 and it gets a named test.
- `RideSessionServiceTests` -- every transition in the section 6 table, no points or distance
  accumulated while paused, spike filter reset on resume from `Suspended` but not from the watching
  modes, settings read once at start.
- `TrackTests` and `ImportRouteTests` -- button visibility per mode, and the preference controls.
- `Fakes.cs` -- a settings repository fake.

## 10. Files

**New**

- `src/RoutePacer.Core/Tracking/StationaryDetector.cs`
- `src/RoutePacer.Core/Domain/AutoPauseSettings.cs`
- `src/RoutePacer.Core/Storage/ISettingsRepository.cs`
- `src/RoutePacer.App/Rides/PauseMode.cs`
- `src/RoutePacer.App/Storage/IndexedDbSettingsRepository.cs`
- `tests/RoutePacer.Core.Tests/Tracking/StationaryDetectorTests.cs`

**Modified**

- `src/RoutePacer.Core/Tracking/PacingService.cs`
- `src/RoutePacer.App/Rides/RideSessionService.cs`
- `src/RoutePacer.App/Rides/TrackerSnapshot.cs`
- `src/RoutePacer.App/Pages/Track.razor`
- `src/RoutePacer.App/Pages/ImportRoute.razor`
- `src/RoutePacer.App/Formatting/RideFormat.cs`
- `src/RoutePacer.App/Program.cs`
- `src/RoutePacer.App/wwwroot/js/storage.js`
- `src/RoutePacer.App/wwwroot/css/tracker.css`
- `tests/RoutePacer.Core.Tests/Tracking/PacingServiceTests.cs`
- `tests/RoutePacer.App.Tests/Rides/RideSessionServiceTests.cs`
- `tests/RoutePacer.App.Tests/Pages/TrackTests.cs`
- `tests/RoutePacer.App.Tests/Pages/ImportRouteTests.cs`
- `tests/RoutePacer.App.Tests/Storage/IndexedDbRepositoryContractTests.cs`
- `tests/RoutePacer.App.Tests/Fakes.cs`

## 11. Consequences

- The ahead/behind reading after any pause changes from what the application does today. This is the
  section 2 defect being corrected, and it is the visible reason the feature works at all.
- A rider who starts a ride and then waits at the start line is auto-paused until they ride off, so
  the pace clock effectively begins on movement. This follows from the feature and is intended.
- Keeping the GPS watch alive through a pause costs battery that today's pause does not. Escalation
  to `Suspended` after five minutes stationary bounds that cost.
- The tracking screen loses its unconditional Pause button. With autopause enabled there is no manual
  pause, per the requirement as stated.
- IndexedDB moves to version 3. The upgrade is additive, but a rider who downgrades to an older build
  afterwards will meet a database newer than that build expects.

## 12. Out of scope

- Making the escalation cutoff configurable. It is a fixed five minutes.
- Changing the autopause preference from the tracking screen.
- Any change to how finished rides are treated: RoutePacer still keeps nothing.
