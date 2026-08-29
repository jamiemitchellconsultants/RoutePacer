---
date: 2026-08-29
slug: design-autopause-and-a-manual-pause-that-ends-when-the-rider-moves
title: "Design autopause and a manual pause that ends when the rider moves"
summary: "**Movement is detected by displacement, not by speed.** `GeoFix.SpeedMps` is nullable, and phones report it unreliably or not at all at walking pace — exactly the condition autopause has to detect."
kind: product
status: accepted
sequence: 2026-08-29T12:20:10.000Z
evidence: "https://github.com/jamiemitchellconsultants/RoutePacer/pull/26; merge commit 37f173eeb59e93315d862922a43085180d2e42d2"
---

## Context

**The ask was two pause controls.** Autopause chosen when a GPX is loaded, with a rider-set stationary time in seconds; and, where autopause is off, a manual pause button on the tracking screen. Both end the same way: *"until the rider starts moving."*

**Reading the code first changed what the work is.** Two findings:

A pause button already exists, at `Track.razor:71`. It stops the GPS watch and releases the wake lock, so it needs a deliberate Resume tap — it cannot end on movement, because it has given up the means to see movement.

And **neither pause pauses the ahead/behind.** `PacingService.Calculate` derives live elapsed straight off the wall clock:

```
live = fix.TimestampUtc - sessionStartedAtUtc
```

`RideSessionService` has tracked `pausedTotal` all along, but subtracts it only inside `CurrentDuration()` — which feeds the displayed *Elapsed* and the stored ride duration, and never reaches the delta. **A rider who pauses at a café watches themselves slide further behind for the whole stop.** The clock they read stops; the number they ride by does not.

So the requested feature could not work as asked until that was fixed, which makes the fix part of this work rather than adjacent to it.

## Decision

**Movement is detected by displacement, not by speed.** `GeoFix.SpeedMps` is nullable, and phones report it unreliably or not at all at walking pace — exactly the condition autopause has to detect. A new pure `StationaryDetector` in `Core.Tracking` holds an anchor and answers from position alone.

**Two radii, not one.** Stationary within **10 m**; moving again past **15 m**. A single radius lets a phone drifting on GPS noise at the boundary flap between paused and running, which a rider reads as the number flickering for no reason they can see.

**The preference is a standing one, not a property of the route.** It lives in its own IndexedDB store, so importing a route does not reset it and a rider who always wants the same autopause sets it once.

**The existing pause is replaced, and escalates.** One button, and it ends on movement — but holding the GPS watch and the wake lock through a long stop is a real battery regression against today's behaviour. So after **5 minutes stationary**, the pause stands the watch down and needs a tap to come out of. That maps cleanly onto the existing rule that a recovered ride comes back paused with GPS off.

**A paused ride records nothing.** Displacement check only: no route match, no appended point, no accumulated distance. An hour parked would otherwise add phantom metres of jitter and corrupt the distance delta the moment the rider set off.

**`PacingService.Calculate` takes `pausedTotal` as a required parameter.** A default would silently preserve today's behaviour at the one call site where it is wrong, which is how this survived as long as it has.

**Paused reads as a word and a brightness, never a hue.** The tracker carries no red or green by deliberate choice ([#22](https://github.com/jamiemitchellconsultants/RoutePacer/pull/22)), and a frozen number that looks identical to a live one is the trap here.

## Consequences

**The ahead/behind reading after any pause changes from what ships today.** This is the defect above being corrected, and it is the visible reason the feature works at all — but a rider who has paused and built a feel for the number afterwards calibrated it on a reading that kept moving while they stood still.

**Autopause makes the pace clock effectively start on movement.** A rider who starts a ride and then waits at the start line is paused until they ride off. That follows from the feature and is intended, but it is a change in when the clock means anything.

**The tracking screen loses its unconditional Pause button.** With autopause on there is no manual pause, per the requirement as stated. `Suspended` offers Resume in either mode, because it is the only way back.

**IndexedDB goes to version 3.** The upgrade is additive and guarded per store, but a rider who downgrades to an older build afterwards meets a database newer than that build expects.

**An E2E test pins the object store list exactly.** `The_imported_route_survives_a_reload_because_it_lives_in_indexeddb` asserts the stores as a set — the schema-level statement that finished rides are not kept. Adding `settings` breaks it by design, so the plan updates that assertion and adds a version 2 to 3 upgrade case built against a hand-seeded version 2 database. The C# contract tests record JS calls and never run the module, so the upgrade is only reachable in a real browser.
