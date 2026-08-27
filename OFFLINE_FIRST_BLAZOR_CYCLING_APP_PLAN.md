# Offline-First Blazor WebAssembly Cycling App — Detailed Implementation Plan

## 1. Goal and Scope

Build a Blazor WebAssembly cycling app optimized for **active-screen ride tracking** (handlebar-mounted phone, screen on), with reliable offline operation for:

- App startup without network
- Route import and reuse offline (`.gpx`, `.fit`)
- Live ride tracking against a reference route
- Real-time lead/lag (distance + time delta)

Out of scope for now:

- Continuous background tracking with screen off
- Native mobile background services

## 2. Product Requirements (MVP)

1. Rider can install app as PWA.
2. Rider can import one or more reference routes (`.gpx` and `.fit`).
3. App persists routes and parsed track data in IndexedDB.
4. Rider can select a reference route and start a live tracking session.
5. App receives high-accuracy GPS updates continuously while screen is active.
6. App computes and displays:
   - Current position on route
   - Distance ahead/behind reference
   - Time ahead/behind reference (`ΔT`)
7. Screen is kept awake during tracking using Wake Lock (best effort, with fallback messaging).
8. Ride session data is stored locally and available offline.
9. App can be launched from RouteTimer with a GPX payload and open directly into import/ready-to-track flow.

## 3. Architecture Overview

### 3.1 Client App

- **Blazor WebAssembly + PWA template**
- UI pages/components:
  - Route Import
  - Route Library
  - Live Tracker Dashboard
  - Ride History

### 3.2 Interop Layer

- `wwwroot/gps.js`:
  - `startTracking(dotNetHelper)`
  - `stopTracking()`
  - `watchPosition` with high-accuracy options
- `wwwroot/wakelock.js`:
  - `acquireWakeLock()`
  - `releaseWakeLock()`
  - visibility-change re-acquire handling
- `wwwroot/invocation.js`:
  - startup extraction of invocation payload from URL and/or Web Share Target
  - handoff of GPX payload metadata/content to Blazor startup service

### 3.3 Domain Services (C#)

- `LocationService` (JS interop callbacks, lifecycle)
- `RouteImportService` (`.gpx` and `.fit` ingestion)
- `RouteMatchingService` (spatial projection, distance along route)
- `PacingService` (target time interpolation, `ΔT`)
- `RideRecordingService` (persist live points and session summary)
- `StorageService` (IndexedDB abstractions)

### 3.4 Persistence

- IndexedDB object stores for:
  - `routes`
  - `route_points`
  - `rides`
  - `ride_points`
  - optional `settings`

## 4. Technology Decisions

1. **Framework**: Blazor WebAssembly with `--pwa`.
2. **GPS Source**: `navigator.geolocation.watchPosition` via JS interop.
3. **Parsing**:
   - `.fit`: `Dynastream.Fit`
   - `.gpx`: `System.Xml.Linq` (or `NETStandard.Gpx` if needed)
4. **Storage**: IndexedDB (prefer an IndexedDB library wrapper over LocalStorage).
5. **Offline strategy**: Service worker app-shell caching + lazy/static assets.
6. **Cross-app invocation from RouteTimer**:
   - Primary: HTTPS deep link with signed, expiring payload reference.
   - Optional mobile PWA enhancement: Web Share Target (`files`/`text`) for direct GPX handoff.

## 4.1 Cross-App Invocation Contract (RouteTimer -> RoutePacer)

Define a stable invocation contract so RouteTimer can open RoutePacer and pass a GPX route safely and predictably.

### Contract v1 (recommended)

- RouteTimer opens:
  - `https://<routepacer-host>/open?src=rt&v=1&payload=<token-or-id>`
- Where `payload` resolves to either:
  1. **Short-lived signed URL** to GPX bytes (preferred when online), or
  2. **Compact encoded GPX content** for small files, or
  3. **Opaque route id** resolvable via shared backend endpoint.

Required query fields:

- `src=rt` (source app marker)
- `v=1` (contract version)
- `payload=<...>` (route handoff)
- `name=<route-name>` (optional, user-friendly)
- `ts=<unix-ms>` (issued timestamp)
- `sig=<signature>` (HMAC or equivalent)

Security constraints:

- Signature required for trust boundary crossing.
- Payload TTL (e.g., 5-15 minutes).
- Reject stale or malformed payloads.
- Never execute arbitrary script/content from payload.

### Optional Contract v2 (PWA Share Target)

On supported mobile browsers:

- RouteTimer uses Web Share to send GPX file.
- RoutePacer registers as share target for `.gpx` and receives file via service worker flow.

Use this as enhancement; keep v1 deep link as baseline for broad compatibility.

## 5. Data Model Plan

Use immutable IDs (GUID/string). Keep metadata and point arrays separated for query efficiency.

### 5.1 Route

- `RouteId`
- `Name`
- `SourceType` (`gpx`/`fit`)
- `ImportedAtUtc`
- `TotalDistanceMeters`
- `TotalDurationSeconds` (if source has timing)
- `BoundingBox` (optional)
- `PointCount`

### 5.2 RoutePoint

- `RouteId`
- `Index`
- `Latitude`
- `Longitude`
- `ElevationMeters` (nullable)
- `DistanceFromStartMeters` (precomputed cumulative)
- `ElapsedSeconds` (nullable if not available)
- `TimestampUtc` (nullable)

### 5.3 Ride Session

- `RideId`
- `RouteId`
- `StartedAtUtc`
- `EndedAtUtc` (nullable while active)
- `TotalDistanceMeters`
- `DurationSeconds`
- `AvgSpeedMps` (derived)

### 5.4 RidePoint

- `RideId`
- `TimestampUtc`
- `Latitude`
- `Longitude`
- `SpeedMps` (nullable)
- `ProjectedRouteDistanceMeters` (nullable until matched)
- `DeltaDistanceMeters` (nullable)
- `DeltaTimeSeconds` (nullable)

## 6. Route Import Pipeline

1. User selects file from browser picker.
2. Validate extension and size constraints.
3. Parse file into normalized `RoutePoint` list.
4. Compute cumulative distance for all points.
5. Normalize timing:
   - If explicit timestamps exist: derive elapsed seconds from first point.
   - If only elapsed-like field exists: map directly.
6. Persist route metadata + points in IndexedDB transaction.
7. Surface parse/storage errors with actionable messages.

### 6.1 Invocation-Based GPX Intake (from RouteTimer)

Add a startup ingestion branch before manual file picker flow:

1. On app startup, inspect URL for invocation parameters (`src`, `v`, `payload`, `sig`, `ts`).
2. Validate contract version and signature/expiry.
3. Resolve payload to GPX content:
   - fetch signed URL, or
   - decode inline payload, or
   - exchange opaque id for GPX bytes.
4. Run the same normalization pipeline used by manual import.
5. Persist route + points to IndexedDB.
6. Navigate user to:
   - route preview/import success screen, then
   - “Start tracking” CTA for the imported route.
7. Clean URL after processing (replaceState) to avoid re-import on refresh.

Failure handling:

- If invocation validation fails, show “Could not import shared route” with reason.
- Offer fallback manual import button.
- Log structured diagnostics for contract mismatch/debug.

Validation rules:

- Minimum point count threshold (e.g., > 2).
- Reject invalid coordinates.
- Handle missing timing gracefully (distance-only mode with no `ΔT`).

## 7. Tracking Pipeline

### 7.1 Session Start

1. User selects route and taps Start.
2. Load route points into memory (or windowed cache for very large tracks).
3. Acquire wake lock.
4. Start GPS watch with high-accuracy options:
   - `enableHighAccuracy: true`
   - `timeout: 5000`
   - `maximumAge: 0`
5. Store session start timestamp.

### 7.2 On GPS Update

For each live point:

1. Validate coordinates and timestamp.
2. Optionally smooth obvious GPS spikes using threshold heuristics.
3. Spatially project live point onto nearest route segment.
4. Compute projected cumulative route distance (`D_target`).
5. Compute live elapsed time from session start (`T_elapsed_live`).
6. Interpolate target elapsed time (`T_target`) at `D_target`.
7. Compute:
   - `ΔDistance = D_live_progress - D_target` (if using live-progress model)
   - `ΔT = T_elapsed_live - T_target`
8. Persist ride point asynchronously to IndexedDB.
9. Update UI model.

### 7.3 Session Stop

1. Stop GPS watch.
2. Release wake lock.
3. Finalize ride summary.
4. Persist session end + aggregates.

## 8. Lead/Lag Computation Details

## 8.1 Spatial Projection (Robust)

Do not use nearest-index matching. For each candidate segment `(Pi -> Pj)`:

1. Convert coordinates to local metric frame (or use geodesic approximation).
2. Project live point `L` onto segment vector.
3. Clamp projection to segment bounds.
4. Compute perpendicular distance to projected point.
5. Select segment with minimal distance (optionally constrained around previous segment index for performance and stability).

Then:

- `D_target = D(Pi_start) + distance(Pi -> projected_point)`

Where `D(Pi_start)` is cumulative distance at segment start.

## 8.2 Temporal Lookup by Distance

Given `D_target`, find bracketing route points:

- `Pk` with `Dk <= D_target`
- `Pk+1` with `Dk+1 >= D_target`

Linear interpolation:

- `ratio = (D_target - Dk) / (Dk+1 - Dk)`
- `T_target = Tk + ratio * (Tk+1 - Tk)`

Then:

- `ΔT = T_elapsed_live - T_target`

Interpretation:

- `ΔT < 0`: rider is ahead
- `ΔT > 0`: rider is behind

## 8.3 Distance Ahead/Behind Semantics

Choose one consistent definition in UI:

1. **Route-progress delta** (recommended for pacing context):
   - Compare rider projected distance at live elapsed time vs expected distance at same elapsed time.
2. **Absolute offset from nearest route location**:
   - Cross-track/along-track spatial offset only.

For this use case, prefer (1) and expose cross-track error separately.

## 9. Performance & Numerical Stability Plan

- Precompute cumulative distances at import time.
- Keep route arrays in contiguous memory-friendly structures.
- Use sliding window around last matched segment for nearest search (e.g., ±N points) to avoid full-scan every update.
- Fallback to full-scan if jump/outlier detected.
- Guard divide-by-zero for tiny segment distances.
- Debounce UI rendering frequency (e.g., process all points but render every 250–500 ms if needed).

## 10. Offline/PWA Plan

1. Create Blazor WASM PWA project (`dotnet new blazorwasm --pwa`).
2. Customize service worker to cache:
   - App shell (HTML/CSS/JS)
   - WASM and framework assets
   - Icons/manifest
3. Define cache versioning strategy for upgrades.
4. Prefer stale-while-revalidate pattern for static assets.
5. Ensure route/ride data stays entirely local in IndexedDB.
6. If Web Share Target is enabled, add manifest/service-worker handling for incoming GPX shares.

Acceptance:

- App loads and starts in airplane mode after first install/open.
- Previously imported routes and rides are available offline.

## 11. Wake Lock Plan

Implement Screen Wake Lock in tracking UI:

1. On Start Tracking: request `navigator.wakeLock.request("screen")`.
2. On Stop Tracking: release lock.
3. On visibility regain: re-request if session is active.
4. Handle unsupported browsers with clear in-app notice.

Important:

- Wake Lock requires secure context (HTTPS/PWA context).
- Must tolerate lock revocation by OS/browser power policies.

## 12. UX Plan (Tracking Screen)

Primary metrics:

- Time ahead/behind (`ΔT`, large and color-coded)
- Distance ahead/behind
- Current speed
- Elapsed ride time
- GPS status/accuracy indicator
- Wake lock status indicator

Secondary:

- Progress bar along route
- Last sync/save status
- Pause/Stop controls with confirmation

Error states:

- GPS unavailable/denied
- Route has no usable points
- Timing not available in source (disable `ΔT`, keep distance mode)

### 12.1 Invocation UX States

- **Incoming route detected**: “Importing route from RouteTimer…”
- **Import success**: show route name, distance, and “Start ride”.
- **Import failed**: clear error reason + retry options:
  - Retry invocation parse
  - Open manual GPX picker

## 13. Security & Privacy Plan

- Keep all ride/route data client-side only.
- Request geolocation permission only when user starts tracking.
- Provide explicit “Delete route/ride” actions.
- Avoid external telemetry by default in MVP.

## 14. Testing Strategy

### 14.1 Unit Tests

- Haversine distance calculations
- Segment projection correctness
- Distance cumulative generation
- Time interpolation edge cases
- `ΔT` sign conventions

### 14.2 Integration Tests

- GPX import end-to-end parse + persist
- FIT import end-to-end parse + persist
- IndexedDB read/write behavior for large route point sets
- GPS callback to UI-state pipeline
- RouteTimer deep-link invocation parse/validate/import flow
- URL cleanup after one-time invocation processing
- Signature expiry and tamper rejection behavior

### 14.3 Manual/PWA Validation

- Offline launch after first load
- Route availability offline
- Wake lock behavior on supported mobile browsers
- Long ride session stability with frequent GPS updates
- Open RoutePacer from RouteTimer and verify auto-import-to-ready flow
- If enabled: share-target GPX handoff on supported devices/browsers

## 15. Delivery Phases

### Phase 1 — Foundation

- PWA scaffold, service worker baseline
- IndexedDB schema and storage service
- Route domain models

### Phase 2 — Import

- GPX parser + import UI
- FIT parser + import UI
- Route library and selection UX
- RouteTimer invocation contract handling and auto-import path

### Phase 3 — Live Tracking Core

- JS GPS bridge + `LocationService`
- Tracking state machine (idle/running/paused/stopped)
- Spatial projection and time interpolation engine

### Phase 4 — Ride Persistence & History

- Persist ride points/summaries
- Ride history listing and basic detail view

### Phase 5 — Hardening

- Wake lock integration and recovery handling
- Performance optimizations for large routes
- Error handling polish and UX improvements
- Invocation security hardening (signature, TTL, replay protection)

## 16. Risks and Mitigations

1. **GPS noise / urban canyon drift**
   - Mitigation: smoothing thresholds + segment windowing + cross-track display.
2. **Sparse timing in source files**
   - Mitigation: distance-only fallback mode with clear UI messaging.
3. **Browser differences (Wake Lock / geolocation accuracy)**
   - Mitigation: capability detection + graceful degradation.
4. **Large route memory usage**
   - Mitigation: preprocessed compact point format + optional chunked loading.

## 17. Definition of Done (MVP)

1. App is installable and launches offline.
2. Rider can import and persist `.gpx` and `.fit` files.
3. Rider can start/stop live GPS tracking while screen remains active.
4. App computes and displays stable distance and time lead/lag relative to selected route.
5. Ride data persists locally and is recoverable after app restart.
6. Core math and import pipelines are covered by targeted automated tests.
7. RouteTimer can invoke RoutePacer with GPX payload and RoutePacer imports it into a ready-to-track state.
