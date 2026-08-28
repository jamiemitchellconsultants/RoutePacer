# RoutePacer architecture

RoutePacer is a hosted Blazor WebAssembly PWA. Everything a rider imports, records, and rides stays on
their device. The server has no API and no database: it serves the published application and two health
probes, and holds no state of any kind.

## Project boundaries

| Project | Responsibility |
|---|---|
| `RoutePacer.Core` | Dependency-free domain: GPX/FIT parsing, normalization, geodesy, matching, pacing. No browser or database types. |
| `RoutePacer.App` | The PWA: IndexedDB persistence, GPS and wake-lock bridges, ride session state machine, UI. |
| `RoutePacer.Server` | Hosts the published PWA, two health probes, and the SPA fallback. Nothing else. |

## Import and normalization

`RouteImportService` enforces `0 < length <= 52,428,800`, selects exactly one parser by file extension, and
hands raw points to `RouteNormalizer`. Every route enters through this one method, whatever file the
rider picked.

`RouteNormalizer` runs one deterministic pass: drop exact consecutive coordinate duplicates, require at
least three remaining points, derive elapsed seconds from timestamps when every point carries one,
otherwise preserve supplied elapsed values, drop timing for the whole track if it is negative or
non-monotonic, accumulate Haversine distance, and reject a zero-length route. Failures carry stable codes
(`invalid-coordinate`, `too-few-points`, `zero-length-route`, `file-too-large`, `unsupported-file`,
`malformed-gpx`, `invalid-gpx-value`, `malformed-fit`, `too-many-points`) so the UI never parses messages.
Missing or broken timing is not an import failure: the route becomes distance-only.

GPX parsing is streaming and hostile-input safe: `DtdProcessing.Prohibit`, a null `XmlResolver`, and a
75,000,000-character ceiling. Both GPX namespaces are handled by reading local names, and `trkpt` and
`rtept` are both accepted.

## Matching and pacing

`RouteMatcher` converts each candidate segment and the fix into an equirectangular frame centred on the
fix, so the projection is metric and antimeridian-safe. For a segment `P0 → P1` and fix `L`:

```text
t     = clamp(dot(L - P0, P1 - P0) / |P1 - P0|², 0, 1)
cross = |P0 + t(P1 - P0) - L|
route = P0.distance + t × (P1.distance - P0.distance)
```

Candidates are scored as `cross + 3 m` when they lie behind the previous match. That penalty is what stops
an out-and-back crossing snapping the rider onto the returning leg, while leaving a clearly closer segment
free to win. The search covers `previous ± 100` segments and falls back to a full scan when there is no
previous match or the windowed result exceeds 75 m. A final cross-track error above 250 m yields no match.

`PacingService` then computes, with `TrackInterpolator` clamping both lookups to the route's start and
finish:

```text
liveElapsed    = max(0, fix.timestamp - session.start)
targetElapsed  = elapsedAtDistance(match.routeDistance)
deltaTime      = liveElapsed - targetElapsed          negative is ahead
expectedDist   = distanceAtElapsed(liveElapsed)
deltaDistance  = match.routeDistance - expectedDist    negative is behind
```

On a route without timing every time-derived field is `null`; the match, speed, and cross-track error are
always preserved, and the UI replaces the time tile with an explanation rather than showing a false zero.

## Ride sessions

States are `Idle → Starting → Running ⇄ Paused → Stopping → Completed`, with `Faulted` for a terminal
failure. Start loads the route, persists a `Running` summary, requests a best-effort wake lock, and only
then starts GPS, so permission is never requested before the rider asks for it. Each accepted fix is
matched, paced, sequenced, and persisted **before** it is published, and publication is throttled to at
most one snapshot per 250 ms, so throttling can never lose a point. Only denied or unsupported geolocation
is terminal; a timeout — which `watchPosition` raises for every five-second coverage gap — surfaces as a
transient status. On startup, any ride left `Running` or `Paused` is finalized as `Interrupted` without
touching GPS.

`GpsSpikeFilter` rejects invalid coordinates, non-increasing timestamps, accuracy worse than 100 m, and an
implied speed above 35 m/s that the browser's own speed contradicts. Accepted coordinates are never
smoothed; raw fixes are retained.

## Browser persistence

IndexedDB database `routepacer`, version 1, with stores `routes` (key `routeId`), `route_points`
(composite key `[routeId, index]`), `rides` (key `rideId`), and `ride_points` (composite key
`[rideId, sequence]`). `route_points` and `ride_points` each carry an index on their parent id. Saving or
deleting an aggregate uses a single read-write transaction across both of its stores, so a route and its
points can never diverge.

## Offline shell

The service worker precaches the generated Blazor asset manifest under a versioned cache prefix and deletes
only its own older caches on activate. Navigations fall back to the cached `index.html`; other same-origin
static GETs are stale-while-revalidate. It bypasses, and never caches, `/health` and every non-GET request. Asset fingerprinting is disabled because the hosted
publish does not substitute fingerprint placeholders in `index.html`; the service worker's versioned cache
and asset manifest provide invalidation instead.
