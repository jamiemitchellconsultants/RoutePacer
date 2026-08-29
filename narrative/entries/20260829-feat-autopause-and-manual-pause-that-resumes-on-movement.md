---
date: 2026-08-29
slug: feat-autopause-and-manual-pause-that-resumes-on-movement
title: "feat: autopause and manual pause that resumes on movement"
summary: "**A pause that ends when the rider moves has to keep watching.** `PauseMode.Manual` and `PauseMode.AutoStationary` hold the GPS watch and the wake lock, because a released watch could not see the movement that ends them."
kind: product
status: accepted
sequence: 2026-08-29T13:28:15.000Z
evidence: "https://github.com/jamiemitchellconsultants/RoutePacer/pull/28; merge commit ad63a2f7bbac974ea9a7ea3619dfefa7e7383397"
---

## Context

The design ([#26](https://github.com/jamiemitchellconsultants/RoutePacer/pull/26)) settled all of this and built none of it. Its central finding was a defect already shipping: `PacingService.Calculate` derived live elapsed from the wall clock, so a rider standing still watched themselves slide further behind for the whole stop. `pausedTotal` had been tracked all along and reached only the displayed *Elapsed*, never the delta. The requested feature could not work until that was fixed, so this pull request is both.

**The plan was split into seven files before any of it was written.** One file per task, one commit per file. The reason is operational rather than architectural: an implementation run can stop part way through — a session ends, a budget runs out — and what matters then is whether the next run can tell exactly where the last one got to. Seven named task files standing against seven commits answer that without anyone having to reconstruct it from a diff. One long plan does not.

## Decision

**A pause that ends when the rider moves has to keep watching.** `PauseMode.Manual` and `PauseMode.AutoStationary` hold the GPS watch and the wake lock, because a released watch could not see the movement that ends them. Only `Suspended` gives them back, and only a tap brings a ride out of it.

**Movement is read from displacement, never from speed.** `StationaryDetector` in `Core.Tracking` is pure and holds an anchor: still within **10 m**, moving again past **15 m**. Two radii rather than one, so a phone drifting on GPS noise at the boundary cannot flap between paused and running — which a rider reads as the number flickering for no reason they can see. The detector observes every running fix whether or not autopause is on, because a manual pause needs an anchor to measure the rider's departure from.

**A paused ride records nothing.** `OnPausedFixAsync` checks displacement and does no route match, appends no point, accumulates no distance. An hour parked would otherwise add phantom metres of jitter and corrupt the distance delta the moment the rider set off. The movement that ends the pause is not counted as ridden distance either: `previousFix` is reset to the departing fix, because the interval was never watched.

**`PacingService.Calculate` takes `pausedTotal` as a required parameter.** A defaulted one would silently preserve today's behaviour at the single call site where it is wrong, which is how this survived as long as it did.

**Autopause ships off, at a 15-second threshold, clamped to 5–300 on both read and write.** It lives in its own IndexedDB `settings` store — one row, keyed by a constant, database version **3** — so importing a route does not reset it and a rider who always wants the same autopause sets it once. The import page saves it on change rather than on import, so the preference survives a file that fails to parse. Unreadable storage falls back to the default and does not stop a ride starting.

**A long stop stands the GPS down after five minutes**, measured from when the rider stopped rather than from when the pause began, so an autopause that engaged at fifteen seconds does not restart the clock. Coming back out resets the spike filter and the segment hint: the first fix after a released watch is arbitrarily far from the last one seen and would otherwise be rejected as a spike.

**The buttons follow the pause mode, not the ride state.** Resume for `Manual` and `Suspended`; Pause only while autopause is off; nothing at all under `AutoStationary`, because riding off is what ends it.

**Frozen reads as a word and a brightness, never a hue.** `RideFormat.PauseDetail` says which pause this is and what ends it, `.pace-delta-muted` drops the value to `opacity: .55`, and a *Paused* row joins the metrics. The tracker carries no red or green by deliberate choice ([#22](https://github.com/jamiemitchellconsultants/RoutePacer/pull/22)), and a frozen number that looks identical to a live one was the trap here.

**Seven commits, one per task**, `task-01-stationary-detector` through `task-07-show-pause-on-tracking-screen`, each with its tests written first. Task 2 lands the pacing fix alone, before any new behaviour is built on top of it.

## Consequences

**The ahead/behind after a pause differs from every build shipped so far.** That is the defect being corrected and the reason the feature works at all, but a rider who has paused before and built a feel for the number calibrated it on a reading that kept moving while they stood still.

**Autopause being off by default makes the manual pause what a rider actually meets.** Turning it on is possible only on the import page, next to the file picker: there is no settings screen, so a rider who wants to change it mid-ride cannot, and a rider who never imports a route never sees the control.

**An autopaused ride offers no button.** Only movement ends it, or five minutes, which escalates to `Suspended` and does offer Resume. That follows from the requirement as stated, and it means a rider autopaused somewhere they cannot ride off from waits out the escalation to get their tracker back.

**IndexedDB goes to version 3.** The upgrade is additive and guarded per store, and an E2E case seeds a hand-built version 2 database to prove a route survives it. A rider who downgrades to an older build afterwards still meets a database newer than that build expects.

**`The_imported_route_survives_a_reload_because_it_lives_in_indexeddb` now expects `settings` in the store list.** That assertion pins the schema as a set on purpose — it is the statement that finished rides are not kept — so adding a store had to break it and be updated deliberately.

**Nothing here has been ridden.** Forty-three new tests cover the state machine, the clamping, the escalation and the wording, but every one of them feeds synthetic fixes. Whether 10 m and 15 m are the right radii, and whether fifteen seconds is the right default, are questions only a real phone on a real road answers. `docs/manual-validation.md` is not updated for autopause and should be.

**Two copies of the plan are now in the tree.** `2026-08-29-autopause-and-manual-pause.md` remains beside the seven task files split out of it, and nothing keeps them in step.
