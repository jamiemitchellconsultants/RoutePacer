---
date: 2026-08-29
slug: feat-hold-one-route-and-stop-keeping-rides
title: "feat: hold one route and stop keeping rides"
summary: "**One route.** Importing replaces it, and `saveRoute` clears and writes in a single IndexedDB transaction — so a failure part way through leaves the previous route intact rather than none at all."
kind: product
status: accepted
sequence: 2026-08-29T07:22:49.000Z
evidence: "https://github.com/jamiemitchellconsultants/RoutePacer/pull/20; merge commit 7f22286cb497a7dc9a6311621b10be10eea3a4a1"
---

## Context

RoutePacer stored a library of routes and a history of every ride: four IndexedDB stores, pages to browse both, and a repository surface to match. None of it served the application's actual job.

The rider already has a head unit or a phone app recording the ride. A second copy here is not a backup — it is a worse recorder with no export, kept on one device, in a browser cache the OS may evict. And a route library implies choosing between routes, when the rider is about to ride exactly one.

## Decision

**One route.** Importing replaces it, and `saveRoute` clears and writes in a single IndexedDB transaction — so a failure part way through leaves the previous route intact rather than none at all. `IRouteRepository` loses its identifier parameters entirely: asking for a route by id would imply a choice the application does not offer. `Routes.razor`, `Rides.razor`, `RideDetail.razor`, `RouteSummaryCard` and `RideSummaryCard` are deleted; `Home` shows the loaded route, and `/track` drops its route id.

**No ride kept.** `rides`/`ride_points` are replaced by `active_ride`/`active_ride_points`, holding one in-progress ride so a reload or an evicted tab cannot end a ride mid-route. Every accepted fix is still written before it is published — that is what makes recovery possible — and stopping clears both stores. The final numbers stay on the tracker page until the rider leaves it.

**IndexedDB goes to version 2**, and the upgrade deletes the version 1 history stores. That is data loss on upgrade, deliberately: leaving old rides on riders' devices would contradict what the app now promises.

**Recovery is restore, not finalise.** A ride left in progress used to be marked `Interrupted` and written to history. It now returns **Paused** with its distance and route progress — never Running, because resuming starts GPS and permission stays the rider's to grant. Elapsed resumes from the last duration actually observed: the interval while the app was gone was never measured, and counting it would inflate every delta on screen. A recovered ride whose route has since been replaced is discarded rather than paced against the wrong route.

Two labels that lied are fixed: **"Stop and save"** saves nothing, and the tracker's **"N points saved on this device"** promised a durability that no longer exists.

## Consequences

**The rider cannot review a ride afterwards.** No distance, duration or average once they leave the tracker page, and no way to answer "what did I ride last week". That capability is gone, not moved — it lives in whatever already records their rides.

`privacy.md` can now separate *what stays* (one route) from *what is not kept at all* (rides), and state that a recovered ride comes back paused so reopening the app never restarts GPS on its own.

`manual-validation.md` gains a section for the cases only real hardware exercises: replacing a route, killing a tab mid-ride and reopening, resuming without an elapsed jump, and replacing the route under a recovered ride.

**This closes #17.** The flaky `A_ride_records_positions_and_survives_a_reload` is deleted along with the history it asserted, replaced by `Stopping_a_ride_keeps_nothing`, which reads `active_ride` directly. The underlying `busy`-guard race that issue describes is untouched and still worth its own look.
