# Offline-First RoutePacer Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build an installable Blazor WebAssembly PWA that imports timed GPX/FIT routes, tracks an active-screen ride entirely offline, shows stable time and distance lead/lag, persists rides locally, and accepts a secure RouteTimer handoff.

**Architecture:** Use a dependency-free `RoutePacer.Core` library for models, parsing contracts, normalization, matching, and pacing; a `RoutePacer.App` Blazor WebAssembly PWA for IndexedDB, browser interop, orchestration, and UI; and focused xUnit, bUnit, and Playwright test projects. Route data is normalized once at import, stored in separate IndexedDB metadata/point stores, loaded as contiguous arrays for tracking, and processed through a session state machine that owns GPS, wake lock, matching, pacing, persistence, and throttled UI snapshots.

**Tech Stack:** .NET 10, Blazor WebAssembly PWA, C# 14, `Dynastream.Fit`, `System.Xml.Linq`, browser IndexedDB, Geolocation API, Screen Wake Lock API, xUnit, FluentAssertions, bUnit, and Microsoft Playwright.

**Spec:** `OFFLINE_FIRST_BLAZOR_CYCLING_APP_PLAN.md`

## Global Constraints

- Tracking targets an active, visible screen; continuous background tracking and native background services remain out of scope.
- The app must start without a network connection after its first successful load/install.
- Route and ride data remain client-side in IndexedDB; no application telemetry is enabled by default.
- Manual import accepts only `.gpx` and `.fit` files, with at least 3 valid points and a maximum file size of 50 MB.
- Geolocation permission is requested only after the rider explicitly starts a ride.
- GPS uses `enableHighAccuracy: true`, `timeout: 5000`, and `maximumAge: 0`.
- Wake lock is best effort, requires a secure context, and must recover after visibility returns when a ride is active.
- Time delta uses `DeltaTimeSeconds = live elapsed time - target elapsed time`; negative means ahead and positive means behind.
- Distance delta uses route-progress semantics: projected rider distance minus expected route distance at the same live elapsed time. Cross-track error is displayed separately.
- A route without usable timing remains trackable in distance-only mode and never fabricates a time delta.
- RouteTimer contract v1 uses `src=rt`, `v=1`, `payload`, `name`, `ts`, and `sig`; accepted payloads expire after 10 minutes and are imported at most once.
- The RoutePacer production origin is `https://pacetracking.tqaentry.com`.
- Web Share Target intake is an enhancement after the v1 HTTPS deep-link path, not an MVP prerequisite.

---

## File and Responsibility Map

| Area | Files | Responsibility |
|---|---|---|
| Solution | `RoutePacer.slnx`, `global.json`, `Directory.Build.props` | Pin .NET 10 and shared build/test policy. |
| Core domain | `src/RoutePacer.Core/Domain/*.cs` | Immutable route, route-point, ride, location, pacing, and state types. |
| Import | `src/RoutePacer.Core/Import/*.cs` | GPX/FIT parsing, validation, normalization, cumulative distance, and timing. |
| Matching/pacing | `src/RoutePacer.Core/Tracking/*.cs` | Metric projection, segment-window matching, temporal interpolation, and lead/lag math. |
| Browser persistence | `src/RoutePacer.App/Storage/*.cs`, `src/RoutePacer.App/wwwroot/js/storage.js` | Versioned IndexedDB schema and typed transactional access. |
| Browser capabilities | `src/RoutePacer.App/Browser/*.cs`, `src/RoutePacer.App/wwwroot/js/{gps,wakelock,invocation}.js` | GPS callbacks, wake lock, URL inspection/cleanup, signature verification. |
| Application workflows | `src/RoutePacer.App/Routes/*.cs`, `src/RoutePacer.App/Rides/*.cs`, `src/RoutePacer.App/Invocation/*.cs` | Import, library, tracking state machine, ride recording, and RouteTimer handoff. |
| UI | `src/RoutePacer.App/Pages/*.razor`, `src/RoutePacer.App/Components/*.razor` | Import, library, tracker, history, status, and failure states. |
| Offline shell | `src/RoutePacer.App/wwwroot/{manifest.webmanifest,service-worker.js,service-worker.published.js}` | Installability, cache versioning, app-shell offline behavior. |
| Tests | `tests/RoutePacer.Core.Tests`, `tests/RoutePacer.App.Tests`, `tests/RoutePacer.E2E` | Pure unit, component/service integration, and real-browser offline/capability validation. |

The implementation is intentionally split into tasks that leave a reviewable, testable capability. Do not combine later UI work into earlier domain tasks.

### Task 1: Scaffold the .NET 10 PWA and Test Boundaries

**Files:**
- Create: `global.json`
- Create: `Directory.Build.props`
- Create: `RoutePacer.slnx`
- Create: `src/RoutePacer.Core/RoutePacer.Core.csproj`
- Create: `src/RoutePacer.App/RoutePacer.App.csproj`
- Create: `src/RoutePacer.App/Program.cs`
- Create: `tests/RoutePacer.Core.Tests/RoutePacer.Core.Tests.csproj`
- Create: `tests/RoutePacer.App.Tests/RoutePacer.App.Tests.csproj`
- Create: `tests/RoutePacer.E2E/RoutePacer.E2E.csproj`
- Modify: `README.md`

**Interfaces:**
- Consumes: none.
- Produces: solution projects targeting `net10.0`; `RoutePacer.App` references `RoutePacer.Core`; test projects reference their production targets.

- [ ] **Step 1: Pin the SDK and create the solution skeleton**

Run:

```bash
dotnet new globaljson --sdk-version 10.0.302 --roll-forward latestFeature
dotnet new sln --name RoutePacer --format slnx
dotnet new classlib --name RoutePacer.Core --output src/RoutePacer.Core --framework net10.0
dotnet new blazorwasm --name RoutePacer.App --output src/RoutePacer.App --framework net10.0 --pwa --no-https
dotnet new xunit --name RoutePacer.Core.Tests --output tests/RoutePacer.Core.Tests --framework net10.0
dotnet new xunit --name RoutePacer.App.Tests --output tests/RoutePacer.App.Tests --framework net10.0
dotnet new xunit --name RoutePacer.E2E --output tests/RoutePacer.E2E --framework net10.0
```

Expected: all six commands exit `0`; the PWA template creates `manifest.webmanifest` and both service-worker files.

- [ ] **Step 2: Add projects and references**

Run:

```bash
dotnet sln RoutePacer.slnx add src/RoutePacer.Core/RoutePacer.Core.csproj src/RoutePacer.App/RoutePacer.App.csproj tests/RoutePacer.Core.Tests/RoutePacer.Core.Tests.csproj tests/RoutePacer.App.Tests/RoutePacer.App.Tests.csproj tests/RoutePacer.E2E/RoutePacer.E2E.csproj
dotnet add src/RoutePacer.App/RoutePacer.App.csproj reference src/RoutePacer.Core/RoutePacer.Core.csproj
dotnet add tests/RoutePacer.Core.Tests/RoutePacer.Core.Tests.csproj reference src/RoutePacer.Core/RoutePacer.Core.csproj
dotnet add tests/RoutePacer.App.Tests/RoutePacer.App.Tests.csproj reference src/RoutePacer.App/RoutePacer.App.csproj
```

- [ ] **Step 3: Add explicit test and import dependencies**

Run:

```bash
dotnet add src/RoutePacer.Core/RoutePacer.Core.csproj package Dynastream.Fit
dotnet add tests/RoutePacer.Core.Tests/RoutePacer.Core.Tests.csproj package FluentAssertions
dotnet add tests/RoutePacer.App.Tests/RoutePacer.App.Tests.csproj package bunit
dotnet add tests/RoutePacer.App.Tests/RoutePacer.App.Tests.csproj package FluentAssertions
dotnet add tests/RoutePacer.E2E/RoutePacer.E2E.csproj package Microsoft.Playwright.Xunit
```

Record the resolved package versions in the generated project files; do not use floating versions.

- [ ] **Step 4: Enable strict shared build settings**

Create `Directory.Build.props`:

```xml
<Project>
  <PropertyGroup>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <AnalysisLevel>latest</AnalysisLevel>
  </PropertyGroup>
</Project>
```

- [ ] **Step 5: Replace template branding and document local commands**

Update the app title/navigation to `RoutePacer`, remove the template counter/weather pages, and add these commands to `README.md`:

```bash
dotnet restore RoutePacer.slnx
dotnet build RoutePacer.slnx --no-restore
dotnet test RoutePacer.slnx --no-build
dotnet run --project src/RoutePacer.App/RoutePacer.App.csproj
```

- [ ] **Step 6: Verify the clean scaffold**

Run:

```bash
dotnet restore RoutePacer.slnx
dotnet build RoutePacer.slnx --no-restore
dotnet test RoutePacer.slnx --no-build
```

Expected: build succeeds with zero warnings; template tests pass.

- [ ] **Step 7: Commit**

```bash
git add global.json Directory.Build.props RoutePacer.slnx src tests README.md
git commit -m "build: scaffold RoutePacer PWA solution"
```

### Task 2: Define the Domain Model and Storage Contract

**Files:**
- Create: `src/RoutePacer.Core/Domain/RouteModels.cs`
- Create: `src/RoutePacer.Core/Domain/RideModels.cs`
- Create: `src/RoutePacer.Core/Domain/TrackingModels.cs`
- Create: `src/RoutePacer.Core/Storage/IRouteRepository.cs`
- Create: `src/RoutePacer.Core/Storage/IRideRepository.cs`
- Create: `tests/RoutePacer.Core.Tests/Domain/DomainInvariantTests.cs`

**Interfaces:**
- Consumes: .NET primitives only.
- Produces: `RouteSummary`, `RoutePoint`, `RouteTrack`, `RideSummary`, `RidePoint`, `GeoFix`, `MatchedPosition`, `PacingSnapshot`, `IRouteRepository`, and `IRideRepository`.

- [ ] **Step 1: Write failing invariant tests**

Create tests proving that a `RouteTrack` rejects fewer than 3 points, mismatched route IDs, non-monotonic point indices/distances, and that `HasTiming` is true only when every point has `ElapsedSeconds`:

```csharp
[Fact]
public void RouteTrack_rejects_non_monotonic_distance()
{
    var points = new[]
    {
        TestPoint(0, 0), TestPoint(1, 100), TestPoint(2, 99)
    };

    var act = () => new RouteTrack(TestSummary(points.Length), points);

    act.Should().Throw<ArgumentException>()
        .WithMessage("*strictly increasing cumulative distance*");
}
```

- [ ] **Step 2: Run the tests to verify failure**

Run: `dotnet test tests/RoutePacer.Core.Tests --filter FullyQualifiedName~DomainInvariantTests`

Expected: FAIL because the domain types do not exist.

- [ ] **Step 3: Implement immutable records and aggregate validation**

Use these exact public shapes:

```csharp
public enum RouteSourceType { Gpx, Fit }

public sealed record RouteSummary(
    Guid RouteId, string Name, RouteSourceType SourceType,
    DateTimeOffset ImportedAtUtc, double TotalDistanceMeters,
    double? TotalDurationSeconds, int PointCount,
    double MinLatitude, double MinLongitude,
    double MaxLatitude, double MaxLongitude);

public sealed record RoutePoint(
    Guid RouteId, int Index, double Latitude, double Longitude,
    double? ElevationMeters, double DistanceFromStartMeters,
    double? ElapsedSeconds, DateTimeOffset? TimestampUtc);

public sealed class RouteTrack
{
    public RouteSummary Summary { get; }
    public IReadOnlyList<RoutePoint> Points { get; }
    public bool HasTiming { get; }
    public RouteTrack(RouteSummary summary, IReadOnlyList<RoutePoint> points);
}
```

```csharp
public enum RideStatus { Running, Paused, Completed, Interrupted }
public sealed record RideSummary(
    Guid RideId, Guid RouteId, DateTimeOffset StartedAtUtc,
    DateTimeOffset? EndedAtUtc, RideStatus Status,
    double TotalDistanceMeters, double DurationSeconds, double AvgSpeedMps);
public sealed record RidePoint(
    Guid RideId, long Sequence, DateTimeOffset TimestampUtc,
    double Latitude, double Longitude, double? SpeedMps,
    double AccuracyMeters, double? ProjectedRouteDistanceMeters,
    double? DeltaDistanceMeters, double? DeltaTimeSeconds,
    double? CrossTrackErrorMeters);
```

```csharp
public sealed record GeoFix(
    DateTimeOffset TimestampUtc, double Latitude, double Longitude,
    double AccuracyMeters, double? SpeedMps);
public sealed record MatchedPosition(
    int SegmentIndex, double RouteDistanceMeters,
    double CrossTrackErrorMeters, double ProjectionRatio);
public sealed record PacingSnapshot(
    DateTimeOffset TimestampUtc, TimeSpan LiveElapsed,
    MatchedPosition Match, double? TargetElapsedSeconds,
    double? DeltaTimeSeconds, double? ExpectedDistanceMeters,
    double? DeltaDistanceMeters, double? SpeedMps);
```

- [ ] **Step 4: Define repository operations around complete transactions**

```csharp
public interface IRouteRepository
{
    Task SaveAsync(RouteTrack route, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RouteSummary>> ListAsync(CancellationToken cancellationToken = default);
    Task<RouteTrack?> GetAsync(Guid routeId, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid routeId, CancellationToken cancellationToken = default);
}

public interface IRideRepository
{
    Task CreateAsync(RideSummary ride, CancellationToken cancellationToken = default);
    Task AppendPointAsync(RidePoint point, CancellationToken cancellationToken = default);
    Task CompleteAsync(RideSummary ride, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RideSummary>> ListAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RidePoint>> GetPointsAsync(Guid rideId, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid rideId, CancellationToken cancellationToken = default);
}
```

- [ ] **Step 5: Run domain tests**

Run: `dotnet test tests/RoutePacer.Core.Tests --filter FullyQualifiedName~DomainInvariantTests`

Expected: PASS for all aggregate and enum behavior.

- [ ] **Step 6: Commit**

```bash
git add src/RoutePacer.Core/Domain src/RoutePacer.Core/Storage tests/RoutePacer.Core.Tests/Domain
git commit -m "feat: define route and ride domain contracts"
```

### Task 3: Implement Geodesy and Route Normalization

**Files:**
- Create: `src/RoutePacer.Core/Import/RawRoutePoint.cs`
- Create: `src/RoutePacer.Core/Import/RouteImportException.cs`
- Create: `src/RoutePacer.Core/Import/RouteNormalizer.cs`
- Create: `src/RoutePacer.Core/Tracking/GeoMath.cs`
- Create: `tests/RoutePacer.Core.Tests/Import/RouteNormalizerTests.cs`
- Create: `tests/RoutePacer.Core.Tests/Tracking/GeoMathTests.cs`

**Interfaces:**
- Consumes: `RouteSourceType`, `RouteSummary`, `RoutePoint`, `RouteTrack`.
- Produces: `RawRoutePoint`; `GeoMath.HaversineMeters`; `RouteNormalizer.Normalize(Guid, string, RouteSourceType, DateTimeOffset, IReadOnlyList<RawRoutePoint>)`.

- [ ] **Step 1: Write failing tests for known distances and normalization**

```csharp
[Theory]
[InlineData(51.5074, -0.1278, 51.5074, -0.1278, 0)]
[InlineData(0, 0, 0, 0.001, 111.195)]
public void Haversine_returns_expected_metres(
    double lat1, double lon1, double lat2, double lon2, double expected)
{
    GeoMath.HaversineMeters(lat1, lon1, lat2, lon2)
        .Should().BeApproximately(expected, 0.2);
}
```

Add normalization cases for cumulative distance, bounding box, timestamps converted relative to the first point, elapsed-only input, invalid coordinates, duplicate consecutive points, and fewer than 3 usable points.

- [ ] **Step 2: Run tests to verify failure**

Run: `dotnet test tests/RoutePacer.Core.Tests --filter "FullyQualifiedName~GeoMathTests|FullyQualifiedName~RouteNormalizerTests"`

Expected: FAIL because `GeoMath` and `RouteNormalizer` do not exist.

- [ ] **Step 3: Implement raw input and Haversine calculation**

```csharp
public sealed record RawRoutePoint(
    double Latitude, double Longitude, double? ElevationMeters,
    double? ElapsedSeconds, DateTimeOffset? TimestampUtc);
```

Use Earth radius `6_371_008.8` metres, radians, and the stable Haversine formula. Reject non-finite coordinates and latitude/longitude outside `[-90, 90]`/`[-180, 180]`.

- [ ] **Step 4: Normalize in one deterministic pass**

`RouteNormalizer.Normalize` must:

1. drop only exact consecutive coordinate duplicates;
2. require at least 3 remaining points;
3. derive elapsed time from timestamps when present, otherwise preserve elapsed input;
4. require non-negative, non-decreasing elapsed values or remove timing from the entire track;
5. compute cumulative Haversine distance and reject a zero-length route;
6. build bounds, totals, and indexed `RoutePoint` records with the supplied route ID and import clock.

Use `RouteImportException` with stable codes `invalid-coordinate`, `too-few-points`, and `zero-length-route` so UI messages do not parse exception text. Invalid or partial timing degrades the whole route to distance-only mode and is not an import failure.

- [ ] **Step 5: Run focused and full core tests**

Run:

```bash
dotnet test tests/RoutePacer.Core.Tests --filter "FullyQualifiedName~GeoMathTests|FullyQualifiedName~RouteNormalizerTests"
dotnet test tests/RoutePacer.Core.Tests
```

Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/RoutePacer.Core/Import src/RoutePacer.Core/Tracking/GeoMath.cs tests/RoutePacer.Core.Tests
git commit -m "feat: normalize route geometry and timing"
```

### Task 4: Parse GPX Routes Securely

**Files:**
- Create: `src/RoutePacer.Core/Import/IRouteFileParser.cs`
- Create: `src/RoutePacer.Core/Import/GpxRouteParser.cs`
- Create: `tests/RoutePacer.Core.Tests/Import/GpxRouteParserTests.cs`
- Create: `tests/RoutePacer.Core.Tests/Fixtures/timed-route.gpx`
- Create: `tests/RoutePacer.Core.Tests/Fixtures/untimed-route.gpx`

**Interfaces:**
- Consumes: `RawRoutePoint`, `RouteImportException`.
- Produces: `IRouteFileParser.CanParse(string)` and `ParseAsync(Stream, CancellationToken)`; `GpxRouteParser` supporting GPX 1.0/1.1 `trkpt` and `rtept` elements.

- [ ] **Step 1: Write parser contract and failing GPX tests**

```csharp
public interface IRouteFileParser
{
    bool CanParse(string fileName);
    Task<IReadOnlyList<RawRoutePoint>> ParseAsync(
        Stream content, CancellationToken cancellationToken = default);
}
```

Test namespace-qualified GPX 1.1, an untimed route, route-point fallback, malformed XML, prohibited DTD, invalid numeric data, cancellation, and a 250,000-point ceiling.

- [ ] **Step 2: Run tests to verify failure**

Run: `dotnet test tests/RoutePacer.Core.Tests --filter FullyQualifiedName~GpxRouteParserTests`

Expected: FAIL because `GpxRouteParser` is absent.

- [ ] **Step 3: Implement streaming-safe XML parsing**

Configure `XmlReaderSettings` exactly as follows:

```csharp
var settings = new XmlReaderSettings
{
    Async = true,
    DtdProcessing = DtdProcessing.Prohibit,
    XmlResolver = null,
    MaxCharactersInDocument = 75_000_000,
    IgnoreComments = true,
    IgnoreWhitespace = true
};
```

Read local names so both GPX namespaces work. Parse `lat`, `lon`, optional `ele`, and optional ISO-8601 `time` using invariant culture. Convert XML, format, and limit failures to `RouteImportException` codes `malformed-gpx`, `invalid-gpx-value`, and `too-many-points`.

- [ ] **Step 4: Run GPX and all core tests**

Run:

```bash
dotnet test tests/RoutePacer.Core.Tests --filter FullyQualifiedName~GpxRouteParserTests
dotnet test tests/RoutePacer.Core.Tests
```

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/RoutePacer.Core/Import tests/RoutePacer.Core.Tests/Import tests/RoutePacer.Core.Tests/Fixtures
git commit -m "feat: parse GPX reference routes"
```

### Task 5: Parse FIT Routes and Share the Import Pipeline

**Files:**
- Create: `src/RoutePacer.Core/Import/FitRouteParser.cs`
- Create: `src/RoutePacer.Core/Import/RouteImportService.cs`
- Create: `src/RoutePacer.Core/Import/ImportedRoute.cs`
- Create: `tests/RoutePacer.Core.Tests/Import/FitRouteParserTests.cs`
- Create: `tests/RoutePacer.Core.Tests/Import/RouteImportServiceTests.cs`
- Create: `tests/RoutePacer.Core.Tests/Fixtures/timed-course.fit`

**Interfaces:**
- Consumes: `Dynastream.Fit`, `IRouteFileParser`, `RouteNormalizer`.
- Produces: `FitRouteParser`; `RouteImportService.ImportAsync(RouteImportRequest, Stream, CancellationToken)` returning `ImportedRoute(RouteTrack Track, string OriginalFileName)`.

- [ ] **Step 1: Write failing FIT and dispatcher tests**

Test semicircle-to-degree conversion, record messages without positions being skipped, timestamp preservation, invalid FIT checksum, `.FIT` case-insensitive dispatch, unsupported extension, 50 MB rejection before parsing, and normalized output equal to equivalent GPX geometry.

```csharp
public sealed record RouteImportRequest(
    string FileName, string? DisplayName, long Length, DateTimeOffset ImportedAtUtc);
public sealed record ImportedRoute(RouteTrack Track, string OriginalFileName);
```

- [ ] **Step 2: Run tests to verify failure**

Run: `dotnet test tests/RoutePacer.Core.Tests --filter "FullyQualifiedName~FitRouteParserTests|FullyQualifiedName~RouteImportServiceTests"`

Expected: FAIL because FIT and orchestration types do not exist.

- [ ] **Step 3: Implement `FitRouteParser`**

Subscribe to `RecordMesg` from the Dynastream decoder, accept records only when both position fields are present, convert FIT semicircles with `degrees = semicircles * (180d / 2147483648d)`, and capture altitude, timestamp, and elapsed-time fields when available. Map decoder failures to `RouteImportException("malformed-fit", ...)` and enforce the same 250,000-point ceiling as GPX.

- [ ] **Step 4: Implement one import dispatcher**

`RouteImportService` receives `IReadOnlyList<IRouteFileParser>`, selects exactly one parser by file name, enforces `0 < Length <= 52_428_800`, creates one `Guid`, derives a safe display name from the file stem when none is supplied, calls `RouteNormalizer`, and returns `ImportedRoute`. Manual and invocation imports must call this same method.

- [ ] **Step 5: Run import tests**

Run:

```bash
dotnet test tests/RoutePacer.Core.Tests --filter "FullyQualifiedName~Import"
dotnet test tests/RoutePacer.Core.Tests
```

Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/RoutePacer.Core/Import tests/RoutePacer.Core.Tests/Import tests/RoutePacer.Core.Tests/Fixtures
git commit -m "feat: import and normalize GPX and FIT routes"
```

### Task 6: Persist Routes and Rides Transactionally in IndexedDB

**Files:**
- Create: `src/RoutePacer.App/Storage/IndexedDbModule.cs`
- Create: `src/RoutePacer.App/Storage/IndexedDbRouteRepository.cs`
- Create: `src/RoutePacer.App/Storage/IndexedDbRideRepository.cs`
- Create: `src/RoutePacer.App/wwwroot/js/storage.js`
- Modify: `src/RoutePacer.App/Program.cs`
- Modify: `src/RoutePacer.App/wwwroot/index.html`
- Create: `tests/RoutePacer.App.Tests/Storage/IndexedDbRepositoryContractTests.cs`

**Interfaces:**
- Consumes: `IRouteRepository`, `IRideRepository`, domain records, `IJSRuntime`.
- Produces: schema version `1` with stores `routes`, `route_points`, `rides`, `ride_points`, and typed repository implementations.

- [ ] **Step 1: Write repository contract tests against a fake JS module**

Verify `SaveAsync` sends summary and points in one call, `GetAsync` rebuilds a valid `RouteTrack`, route deletion requests a route/point transaction, ride append preserves sequence, and completion replaces the running summary.

```csharp
[Fact]
public async Task SaveAsync_sends_route_and_points_as_one_transaction()
{
    var module = new RecordingIndexedDbModule();
    var repository = new IndexedDbRouteRepository(module);

    await repository.SaveAsync(RouteFixtures.TimedTrack());

    module.Calls.Should().ContainSingle(c => c.Name == "saveRoute");
}
```

- [ ] **Step 2: Run tests to verify failure**

Run: `dotnet test tests/RoutePacer.App.Tests --filter FullyQualifiedName~IndexedDbRepositoryContractTests`

Expected: FAIL because persistence classes are absent.

- [ ] **Step 3: Implement the versioned JavaScript database module**

Export `openDatabase`, `saveRoute`, `listRoutes`, `getRoute`, `deleteRoute`, `createRide`, `appendRidePoint`, `completeRide`, `listRides`, `getRidePoints`, and `deleteRide`. Use database name `routepacer`, version `1`, composite keys `[routeId,index]` and `[rideId,sequence]`, indexes `routeId`/`rideId`, and a single read-write transaction for each aggregate save/delete. Reject the promise on request or transaction error with the operation and browser error name.

- [ ] **Step 4: Implement a lazy module wrapper and repositories**

```csharp
public interface IIndexedDbModule : IAsyncDisposable
{
    ValueTask<T?> InvokeAsync<T>(string identifier, object?[]? args = null);
    ValueTask InvokeVoidAsync(string identifier, object?[]? args = null);
}
```

`IndexedDbModule` imports `./js/storage.js` once using `Lazy<Task<IJSObjectReference>>`. Repository methods pass lower-camel JSON DTOs and use GUIDs as lowercase `D` strings so JavaScript keys are stable.

- [ ] **Step 5: Register scoped storage services**

```csharp
builder.Services.AddScoped<IIndexedDbModule, IndexedDbModule>();
builder.Services.AddScoped<IRouteRepository, IndexedDbRouteRepository>();
builder.Services.AddScoped<IRideRepository, IndexedDbRideRepository>();
```

- [ ] **Step 6: Run service tests and build**

Run:

```bash
dotnet test tests/RoutePacer.App.Tests --filter FullyQualifiedName~IndexedDbRepositoryContractTests
dotnet build RoutePacer.slnx
```

Expected: PASS with zero warnings.

- [ ] **Step 7: Commit**

```bash
git add src/RoutePacer.App/Storage src/RoutePacer.App/wwwroot/js/storage.js src/RoutePacer.App/Program.cs src/RoutePacer.App/wwwroot/index.html tests/RoutePacer.App.Tests/Storage
git commit -m "feat: persist routes and rides in IndexedDB"
```

### Task 7: Build Manual Import and Route Library Workflows

**Files:**
- Create: `src/RoutePacer.App/Routes/RouteCatalogService.cs`
- Create: `src/RoutePacer.App/Pages/ImportRoute.razor`
- Create: `src/RoutePacer.App/Pages/Routes.razor`
- Create: `src/RoutePacer.App/Components/RouteSummaryCard.razor`
- Modify: `src/RoutePacer.App/Layout/NavMenu.razor`
- Modify: `src/RoutePacer.App/Program.cs`
- Create: `tests/RoutePacer.App.Tests/Routes/RouteCatalogServiceTests.cs`
- Create: `tests/RoutePacer.App.Tests/Pages/ImportRouteTests.cs`
- Create: `tests/RoutePacer.App.Tests/Pages/RoutesTests.cs`

**Interfaces:**
- Consumes: `RouteImportService`, `IRouteRepository`, `IBrowserFile`.
- Produces: `RouteCatalogService.ImportAsync(string, string?, long, Stream, DateTimeOffset, CancellationToken)`; routes `/import` and `/routes`; navigation to `/track/{routeId}`.

- [ ] **Step 1: Write failing workflow and component tests**

Cover file accept filter `.gpx,.fit`, the 50 MB stream cap, busy-state double-submit prevention, actionable messages for every stable import error code, persistence only after parse succeeds, library ordering by newest import, timing/distance-only badge, delete confirmation, and Start Ride link.

- [ ] **Step 2: Run tests to verify failure**

Run: `dotnet test tests/RoutePacer.App.Tests --filter "FullyQualifiedName~RouteCatalogServiceTests|FullyQualifiedName~ImportRouteTests|FullyQualifiedName~RoutesTests"`

Expected: FAIL because pages and service are absent.

- [ ] **Step 3: Implement the application service**

```csharp
public sealed class RouteCatalogService(
    RouteImportService importer, IRouteRepository routes)
{
    public async Task<RouteSummary> ImportAsync(
        string fileName, string? displayName, long length, Stream content,
        DateTimeOffset importedAtUtc, CancellationToken cancellationToken = default)
    {
        var imported = await importer.ImportAsync(
            new(fileName, displayName, length, importedAtUtc), content, cancellationToken);
        await routes.SaveAsync(imported.Track, cancellationToken);
        return imported.Track.Summary;
    }
}
```

- [ ] **Step 4: Implement import and library UI states**

`ImportRoute.razor` shows idle, parsing, saving, success, and failure states; success renders name, formatted kilometres, point count, timing availability, `Start ride`, and `View routes`. `Routes.razor` loads on initialization, shows an offline-safe empty state, and only removes a card after repository deletion succeeds.

- [ ] **Step 5: Register parsers and service**

```csharp
builder.Services.AddSingleton<IRouteFileParser, GpxRouteParser>();
builder.Services.AddSingleton<IRouteFileParser, FitRouteParser>();
builder.Services.AddSingleton<RouteNormalizer>();
builder.Services.AddSingleton<RouteImportService>();
builder.Services.AddScoped<RouteCatalogService>();
```

- [ ] **Step 6: Run component tests and full build**

Run:

```bash
dotnet test tests/RoutePacer.App.Tests --filter "FullyQualifiedName~Routes|FullyQualifiedName~ImportRoute"
dotnet build RoutePacer.slnx
```

Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add src/RoutePacer.App/Routes src/RoutePacer.App/Pages src/RoutePacer.App/Components src/RoutePacer.App/Layout src/RoutePacer.App/Program.cs tests/RoutePacer.App.Tests
git commit -m "feat: add route import and offline library"
```

### Task 8: Implement RouteTimer Contract v1 Intake

**Files:**
- Create: `docs/contracts/route-timer-invocation-v1.md`
- Create: `src/RoutePacer.App/Invocation/InvocationRequest.cs`
- Create: `src/RoutePacer.App/Invocation/InvocationParser.cs`
- Create: `src/RoutePacer.App/Invocation/IInvocationVerifier.cs`
- Create: `src/RoutePacer.App/Invocation/WebCryptoInvocationVerifier.cs`
- Create: `src/RoutePacer.App/Invocation/RouteTimerInvocationService.cs`
- Create: `src/RoutePacer.App/Pages/Open.razor`
- Create: `src/RoutePacer.App/wwwroot/js/invocation.js`
- Modify: `src/RoutePacer.App/Program.cs`
- Create: `tests/RoutePacer.App.Tests/Invocation/InvocationParserTests.cs`
- Create: `tests/RoutePacer.App.Tests/Invocation/RouteTimerInvocationServiceTests.cs`

**Interfaces:**
- Consumes: `RouteCatalogService`, `HttpClient`, `TimeProvider`, browser history API.
- Produces: `InvocationRequest(string Source, int Version, Uri PayloadUri, string? Name, long IssuedUnixMilliseconds, string Signature)`; `IInvocationVerifier.VerifyAsync(InvocationRequest, CancellationToken)`; `/open` import-to-ready flow.

- [ ] **Step 1: Freeze the interoperable contract in a checked-in document**

Specify this canonical signed byte sequence with UTF-8 encoding:

```text
rt\n1\n{payload-absolute-uri}\n{name-or-empty}\n{unix-milliseconds}
```

Use ECDSA P-256 with SHA-256 and base64url IEEE-P1363 signature bytes. RouteTimer owns the private key; RoutePacer embeds only the public JWK. This replaces a shared HMAC secret because any secret shipped in WebAssembly is public. The payload must be an absolute HTTPS URL on an allowlisted RouteTimer host, return `application/gpx+xml`, be at most 50 MB, and may be consumed once. Include one fixed signed valid query and one tampered fixture for cross-repository contract tests.

- [ ] **Step 2: Write failing parse, expiry, tamper, and import tests**

Test missing/duplicate query keys, `src != rt`, `v != 1`, malformed timestamp/signature, more than 10 minutes old, more than 60 seconds in the future, non-HTTPS payload, disallowed host, invalid signature, non-GPX content type, oversized download, one fetch only, successful persistence, navigation to `/track/{routeId}?ready=1`, and URL cleanup via `history.replaceState({}, "", "/open")` after terminal success or failure.

- [ ] **Step 3: Implement strict parsing and public-key verification**

```csharp
public interface IInvocationVerifier
{
    ValueTask<bool> VerifyAsync(
        InvocationRequest request, CancellationToken cancellationToken = default);
}
```

`invocation.js` imports the configured public JWK with `crypto.subtle.importKey`, converts base64url signature bytes, and calls `crypto.subtle.verify({ name: "ECDSA", hash: "SHA-256" }, key, signature, canonicalBytes)`. No private or symmetric signing material is stored in RoutePacer.

- [ ] **Step 4: Implement download and shared import orchestration**

Use `HttpCompletionOption.ResponseHeadersRead`, require a successful response and `application/gpx+xml` or `application/octet-stream`, reject `Content-Length > 52_428_800`, wrap the response stream in a counting stream for missing/false lengths, and pass it to `RouteCatalogService.ImportAsync` as `<name>.gpx`. Map failures to explicit user messages while logging only error code, source, version, and host—never the token, signature, query string, or GPX bytes.

- [ ] **Step 5: Implement `/open` states and fallback**

Render `Importing route from RouteTimer…`, the imported name/distance plus `Start ride`, or `Could not import shared route: {safe reason}` with `Retry` and `Choose GPX file`. Retry reuses the in-memory parsed request only before a payload is successfully consumed. Cleanup the address bar after parsing so refresh cannot re-import.

- [ ] **Step 6: Register configuration and services**

Add non-secret settings to `wwwroot/appsettings.json`:

```json
{
  "RouteTimerInvocation": {
    "Enabled": false,
    "AllowedPayloadHosts": ["routetimer.tqaentry.com"],
    "MaximumAgeMinutes": 10,
    "PublicKeyJwk": ""
  }
}
```

Bind and validate settings at startup: an empty key is valid only while `Enabled` is `false`; enabling intake without a valid P-256 public JWK must fail startup. Tests generate a fixed fixture key pair. The coordinated rollout in Task 16 exports RouteTimer's real public JWK into the production configuration and changes `Enabled` to `true`; implementation must not invent signing material or ship a symmetric secret.

- [ ] **Step 7: Run invocation tests**

Run:

```bash
dotnet test tests/RoutePacer.App.Tests --filter FullyQualifiedName~Invocation
dotnet build RoutePacer.slnx
```

Expected: PASS; no signing secret appears in `src/RoutePacer.App/wwwroot`.

- [ ] **Step 8: Commit**

```bash
git add docs/contracts src/RoutePacer.App/Invocation src/RoutePacer.App/Pages/Open.razor src/RoutePacer.App/wwwroot/js/invocation.js src/RoutePacer.App/wwwroot/appsettings.json src/RoutePacer.App/Program.cs tests/RoutePacer.App.Tests/Invocation
git commit -m "feat: accept secure RouteTimer route handoffs"
```

### Task 9: Implement Spatial Route Matching

**Files:**
- Create: `src/RoutePacer.Core/Tracking/RouteMatcher.cs`
- Create: `src/RoutePacer.Core/Tracking/RouteMatcherOptions.cs`
- Create: `tests/RoutePacer.Core.Tests/Tracking/RouteMatcherTests.cs`

**Interfaces:**
- Consumes: `RouteTrack`, `GeoFix`, `MatchedPosition`, `GeoMath`.
- Produces: `MatchedPosition? RouteMatcher.Match(RouteTrack, GeoFix, int? previousSegmentIndex)` and `RouteMatcherOptions(WindowSegments: 100, FullScanThresholdMeters: 75, MaximumCrossTrackMeters: 250)`.

- [ ] **Step 1: Write failing projection and stability tests**

Cover projection before/middle/after a segment, clamped ratio, cumulative route distance, zero-length segment skipping, antimeridian-safe local metric conversion, previous-index window selection, full-scan fallback when the best window result exceeds 75 m, overlapping out-and-back geometry preferring forward continuity, and rejection when cross-track error exceeds 250 m.

```csharp
[Fact]
public void Match_projects_onto_segment_not_nearest_vertex()
{
    var match = _matcher.Match(RouteFixtures.StraightOneKilometre(),
        new GeoFix(_now, 0.0001, 0.0045, 5, null), null);

    match.RouteDistanceMeters.Should().BeApproximately(500, 2);
    match.ProjectionRatio.Should().BeApproximately(0.5, 0.01);
}
```

- [ ] **Step 2: Run tests to verify failure**

Run: `dotnet test tests/RoutePacer.Core.Tests --filter FullyQualifiedName~RouteMatcherTests`

Expected: FAIL because matcher types do not exist.

- [ ] **Step 3: Implement metric segment projection**

Convert each candidate segment and fix into an equirectangular local frame centred on the fix, calculate `t = dot(L-P0, P1-P0) / |P1-P0|²`, clamp to `[0,1]`, and select minimum perpendicular distance. Derive route distance as the segment start cumulative distance plus `t` times the segment route-length delta.

- [ ] **Step 4: Add the window/full-scan policy**

Search segments `previous ± 100` first; full-scan when there is no previous match or window cross-track exceeds 75 m. For candidates within 3 m of the best cross-track error, prefer the smallest non-negative segment-index change to avoid snapping backward at crossings. Return `null` when the final cross-track error exceeds 250 m.

- [ ] **Step 5: Run matcher and full core tests**

Run:

```bash
dotnet test tests/RoutePacer.Core.Tests --filter FullyQualifiedName~RouteMatcherTests
dotnet test tests/RoutePacer.Core.Tests
```

Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/RoutePacer.Core/Tracking tests/RoutePacer.Core.Tests/Tracking
git commit -m "feat: match live positions to route segments"
```

### Task 10: Implement Time and Distance Pacing

**Files:**
- Create: `src/RoutePacer.Core/Tracking/PacingService.cs`
- Create: `src/RoutePacer.Core/Tracking/TrackInterpolator.cs`
- Create: `tests/RoutePacer.Core.Tests/Tracking/PacingServiceTests.cs`
- Create: `tests/RoutePacer.Core.Tests/Tracking/TrackInterpolatorTests.cs`

**Interfaces:**
- Consumes: `RouteTrack`, `MatchedPosition`, `GeoFix`, `PacingSnapshot`.
- Produces: `TrackInterpolator.ElapsedAtDistance`, `TrackInterpolator.DistanceAtElapsed`, and `PacingService.Calculate(RouteTrack, MatchedPosition, DateTimeOffset sessionStartedAtUtc, GeoFix)`.

- [ ] **Step 1: Write failing interpolation and sign-convention tests**

Test exact points, between points, before zero, beyond finish, repeated distances, repeated elapsed values, negative/positive delta time, expected distance at live elapsed, route-progress delta, finish overrun, and all time-derived fields `null` for an untimed route.

```csharp
[Theory]
[InlineData(90, 100, -10)]
[InlineData(110, 100, 10)]
public void Delta_time_is_live_minus_target(double live, double target, double expected)
{
    PacingService.DeltaTime(live, target).Should().Be(expected);
}
```

- [ ] **Step 2: Run tests to verify failure**

Run: `dotnet test tests/RoutePacer.Core.Tests --filter "FullyQualifiedName~PacingServiceTests|FullyQualifiedName~TrackInterpolatorTests"`

Expected: FAIL because pacing types are absent.

- [ ] **Step 3: Implement binary-search interpolation**

`ElapsedAtDistance` brackets cumulative distance and linearly interpolates elapsed time. `DistanceAtElapsed` brackets elapsed seconds and linearly interpolates cumulative distance. Clamp both lookups to route start/finish and guard a zero denominator by returning the upper bracket value.

- [ ] **Step 4: Calculate the complete snapshot**

Compute `liveElapsed = max(0, fix.TimestampUtc - sessionStartedAtUtc)`, `targetElapsed`, `deltaTime = live - target`, `expectedDistance`, and `deltaDistance = match.RouteDistanceMeters - expectedDistance`. Always preserve match, speed, and cross-track fields; set all timing/expected-distance/delta fields to `null` in distance-only mode.

- [ ] **Step 5: Run pacing and full core tests**

Run:

```bash
dotnet test tests/RoutePacer.Core.Tests --filter "FullyQualifiedName~PacingServiceTests|FullyQualifiedName~TrackInterpolatorTests"
dotnet test tests/RoutePacer.Core.Tests
```

Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/RoutePacer.Core/Tracking tests/RoutePacer.Core.Tests/Tracking
git commit -m "feat: calculate route time and distance lead lag"
```

### Task 11: Bridge GPS and Wake Lock Browser Capabilities

**Files:**
- Create: `src/RoutePacer.App/Browser/ILocationService.cs`
- Create: `src/RoutePacer.App/Browser/LocationService.cs`
- Create: `src/RoutePacer.App/Browser/IWakeLockService.cs`
- Create: `src/RoutePacer.App/Browser/WakeLockService.cs`
- Create: `src/RoutePacer.App/Browser/BrowserCapabilityStatus.cs`
- Create: `src/RoutePacer.App/wwwroot/js/gps.js`
- Create: `src/RoutePacer.App/wwwroot/js/wakelock.js`
- Modify: `src/RoutePacer.App/Program.cs`
- Create: `tests/RoutePacer.App.Tests/Browser/LocationServiceTests.cs`
- Create: `tests/RoutePacer.App.Tests/Browser/WakeLockServiceTests.cs`

**Interfaces:**
- Consumes: `GeoFix`, JS module interop.
- Produces: `ILocationService.StartAsync(Func<GeoFix, Task>, Func<LocationFailure, Task>, CancellationToken)`, `StopAsync`; `IWakeLockService.AcquireAsync`, `ReleaseAsync`; observable capability status.

- [ ] **Step 1: Write failing lifecycle and callback tests**

Test exact GPS options, no second watch while active, conversion of epoch milliseconds, invalid/non-finite callback rejection, error-code mapping for permission denied/unavailable/timeout, idempotent stop/dispose, unsupported wake lock status, acquire/release, revoked state, and visibility re-acquire only when tracking remains requested.

- [ ] **Step 2: Run tests to verify failure**

Run: `dotnet test tests/RoutePacer.App.Tests --filter "FullyQualifiedName~LocationServiceTests|FullyQualifiedName~WakeLockServiceTests"`

Expected: FAIL because browser services are absent.

- [ ] **Step 3: Implement `gps.js`**

Export `startTracking(dotNetReference)` and `stopTracking()`. Keep one watch ID; call `navigator.geolocation.watchPosition` with:

```javascript
{ enableHighAccuracy: true, timeout: 5000, maximumAge: 0 }
```

Forward timestamp, latitude, longitude, accuracy, and nullable speed to `[JSInvokable] OnPosition`; forward numeric error code and safe message to `OnError`. Feature-detect geolocation before starting.

- [ ] **Step 4: Implement `wakelock.js`**

Maintain `requested`, `sentinel`, and the .NET reference. `acquireWakeLock` sets requested before feature detection, requests `screen`, reports `acquired`/`unsupported`/`revoked`/`failed`, and attaches the release listener. `visibilitychange` reacquires only when `requested && document.visibilityState === "visible" && !sentinel`. `releaseWakeLock` clears requested first and releases safely.

- [ ] **Step 5: Implement scoped C# wrappers**

Keep `DotNetObjectReference` alive for the whole watch/request lifetime, marshal callbacks through the injected delegates, and guarantee JS stop/release in `DisposeAsync`. Expose status changes via typed events so the tracker component does not interpret strings.

- [ ] **Step 6: Register and test**

Run:

```bash
dotnet test tests/RoutePacer.App.Tests --filter "FullyQualifiedName~Browser"
dotnet build RoutePacer.slnx
```

Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add src/RoutePacer.App/Browser src/RoutePacer.App/wwwroot/js/gps.js src/RoutePacer.App/wwwroot/js/wakelock.js src/RoutePacer.App/Program.cs tests/RoutePacer.App.Tests/Browser
git commit -m "feat: add GPS and wake lock browser bridges"
```

### Task 12: Record and Recover Ride Sessions

**Files:**
- Create: `src/RoutePacer.App/Rides/RideSessionState.cs`
- Create: `src/RoutePacer.App/Rides/RideSessionService.cs`
- Create: `src/RoutePacer.App/Rides/GpsSpikeFilter.cs`
- Create: `src/RoutePacer.App/Rides/TrackerSnapshot.cs`
- Modify: `src/RoutePacer.App/Program.cs`
- Create: `tests/RoutePacer.App.Tests/Rides/RideSessionServiceTests.cs`
- Create: `tests/RoutePacer.App.Tests/Rides/GpsSpikeFilterTests.cs`

**Interfaces:**
- Consumes: route/ride repositories, location/wake services, `RouteMatcher`, `PacingService`, `TimeProvider`.
- Produces: `StartAsync(Guid)`, `PauseAsync`, `ResumeAsync`, `StopAsync`; states `Idle`, `Starting`, `Running`, `Paused`, `Stopping`, `Completed`, `Faulted`; `SnapshotChanged` event throttled to at most every 250 ms.

- [ ] **Step 1: Write state-machine tests before implementation**

Cover missing route, invalid transition, session persisted before GPS begins, GPS permission failure finalizing as `Interrupted`, normal start/pause/resume/stop ordering, wake lock failure remaining non-fatal, every accepted fix persisted, UI throttling without point loss, match loss outside 250 m, restart recovery of running rides as `Interrupted`, aggregate duration excluding paused intervals, and average speed from accepted movement.

- [ ] **Step 2: Run tests to verify failure**

Run: `dotnet test tests/RoutePacer.App.Tests --filter "FullyQualifiedName~RideSessionServiceTests|FullyQualifiedName~GpsSpikeFilterTests"`

Expected: FAIL because ride workflow types are absent.

- [ ] **Step 3: Implement a conservative GPS spike filter**

Accept the first fix. Reject invalid coordinates, non-increasing timestamps, accuracy over 100 m, and implied speed over 35 m/s when the browser speed is absent or agrees within 10 m/s. Do not smooth accepted coordinates; retain raw fixes for auditability.

- [ ] **Step 4: Implement ordered session lifecycle**

Start loads the route, creates the running summary, requests wake lock, then starts GPS. Each accepted fix is matched, paced, converted to a monotonically sequenced `RidePoint`, persisted, and then published. Pause stops GPS and releases wake lock; resume reacquires both while preserving elapsed/paused accounting. Stop halts browser services before finalizing summary.

- [ ] **Step 5: Implement crash recovery**

On app startup, list rides and convert every `Running`/`Paused` summary to `Interrupted` with `EndedAtUtc = TimeProvider.GetUtcNow()`. Never auto-resume GPS or request permission during recovery.

- [ ] **Step 6: Run ride tests and full solution tests**

Run:

```bash
dotnet test tests/RoutePacer.App.Tests --filter FullyQualifiedName~Rides
dotnet test RoutePacer.slnx
```

Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add src/RoutePacer.App/Rides src/RoutePacer.App/Program.cs tests/RoutePacer.App.Tests/Rides
git commit -m "feat: record resilient offline ride sessions"
```

### Task 13: Build the Live Tracker Dashboard

**Files:**
- Create: `src/RoutePacer.App/Pages/Track.razor`
- Create: `src/RoutePacer.App/Components/PaceDelta.razor`
- Create: `src/RoutePacer.App/Components/TrackingStatus.razor`
- Create: `src/RoutePacer.App/Formatting/RideFormat.cs`
- Create: `src/RoutePacer.App/wwwroot/css/tracker.css`
- Create: `tests/RoutePacer.App.Tests/Pages/TrackTests.cs`
- Create: `tests/RoutePacer.App.Tests/Formatting/RideFormatTests.cs`

**Interfaces:**
- Consumes: `RideSessionService`, `TrackerSnapshot`, route ID from `/track/{RouteId:guid}`.
- Produces: handlebar-readable live metrics, capability/error states, pause/resume/stop actions, and navigation to completed ride detail.

- [ ] **Step 1: Write failing format and component tests**

Test signed time/distance labels (`2:03 ahead`, `0:45 behind`, `120 m ahead`), neutral zero, null timing (`Timing unavailable`), current speed, elapsed time, progress clamp, GPS accuracy categories, wake status, start confirmation, button transition guards, stop confirmation, and accessible live regions.

- [ ] **Step 2: Run tests to verify failure**

Run: `dotnet test tests/RoutePacer.App.Tests --filter "FullyQualifiedName~TrackTests|FullyQualifiedName~RideFormatTests"`

Expected: FAIL because tracker UI is absent.

- [ ] **Step 3: Implement deterministic formatting**

Use invariant calculations and localized display text: negative deltas render `ahead`, positive render `behind`, absolute values are formatted, speed converts m/s to km/h, elapsed uses `h:mm:ss`, and accuracy is `Good` at ≤10 m, `Fair` at ≤30 m, otherwise `Poor`.

- [ ] **Step 4: Implement the dashboard hierarchy**

Render time delta largest, distance delta second, then speed, elapsed, GPS accuracy, cross-track error, route progress, local-save state, and wake status. Use text/icon plus color so meaning is not color-only. For distance-only routes, replace the time tile with an explanation instead of a numeric zero.

- [ ] **Step 5: Wire lifecycle safely**

Subscribe/unsubscribe `SnapshotChanged` in component lifecycle, invoke UI updates through `InvokeAsync`, disable commands during transitions, prompt before Stop, and stop the ride on component disposal only after an explicit user stop—not on navigation or visibility loss.

- [ ] **Step 6: Run UI tests and build**

Run:

```bash
dotnet test tests/RoutePacer.App.Tests --filter "FullyQualifiedName~Track|FullyQualifiedName~RideFormat"
dotnet build RoutePacer.slnx
```

Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add src/RoutePacer.App/Pages/Track.razor src/RoutePacer.App/Components src/RoutePacer.App/Formatting src/RoutePacer.App/wwwroot/css/tracker.css tests/RoutePacer.App.Tests
git commit -m "feat: add live pacing dashboard"
```

### Task 14: Add Ride History, Detail, and Explicit Deletion

**Files:**
- Create: `src/RoutePacer.App/Pages/Rides.razor`
- Create: `src/RoutePacer.App/Pages/RideDetail.razor`
- Create: `src/RoutePacer.App/Components/RideSummaryCard.razor`
- Modify: `src/RoutePacer.App/Layout/NavMenu.razor`
- Create: `tests/RoutePacer.App.Tests/Pages/RidesTests.cs`
- Create: `tests/RoutePacer.App.Tests/Pages/RideDetailTests.cs`
- Modify: `tests/RoutePacer.App.Tests/Pages/RoutesTests.cs`

**Interfaces:**
- Consumes: `IRideRepository`, `IRouteRepository`, `RideFormat`.
- Produces: `/rides`, `/rides/{RideId:guid}`, ride deletion, and route deletion blocked while rides reference the route.

- [ ] **Step 1: Write failing history/privacy tests**

Cover newest-first history, completed/interrupted badges, duration/distance/average speed, empty state, detail point count and final deltas, missing ride, deletion confirmation, removal only after persistence succeeds, and a route delete message that requires deleting its rides first.

- [ ] **Step 2: Run tests to verify failure**

Run: `dotnet test tests/RoutePacer.App.Tests --filter "FullyQualifiedName~RidesTests|FullyQualifiedName~RideDetailTests|FullyQualifiedName~RoutesTests"`

Expected: FAIL for missing history pages and route reference guard.

- [ ] **Step 3: Implement list and detail pages**

Show only locally stored data. Detail includes route name when present, timestamps, aggregates, accepted GPS point count, last pacing values, and a clear `Interrupted` explanation. Do not add map tiles or network-backed assets.

- [ ] **Step 4: Enforce explicit, consistent deletion**

Before route deletion, query rides and block when any summary references the route. Ride deletion removes summary and all `ride_points` in one IndexedDB transaction. Confirmation text names the record and states that deletion cannot be undone.

- [ ] **Step 5: Run page tests**

Run:

```bash
dotnet test tests/RoutePacer.App.Tests --filter FullyQualifiedName~Pages
dotnet test RoutePacer.slnx
```

Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/RoutePacer.App/Pages src/RoutePacer.App/Components src/RoutePacer.App/Layout tests/RoutePacer.App.Tests/Pages
git commit -m "feat: add local ride history and deletion"
```

### Task 15: Harden the PWA Shell and Prove Offline Operation

**Files:**
- Modify: `src/RoutePacer.App/wwwroot/manifest.webmanifest`
- Modify: `src/RoutePacer.App/wwwroot/service-worker.js`
- Modify: `src/RoutePacer.App/wwwroot/service-worker.published.js`
- Modify: `src/RoutePacer.App/wwwroot/index.html`
- Create: `src/RoutePacer.App/wwwroot/icons/icon-192.png`
- Create: `src/RoutePacer.App/wwwroot/icons/icon-512.png`
- Create: `tests/RoutePacer.E2E/OfflinePwaTests.cs`
- Create: `tests/RoutePacer.E2E/TrackingCapabilityTests.cs`
- Create: `tests/RoutePacer.E2E/RouteTimerInvocationTests.cs`
- Create: `tests/RoutePacer.E2E/Fixtures/timed-route.gpx`
- Create: `docs/manual-validation.md`

**Interfaces:**
- Consumes: published PWA, browser context permissions, fixture routes.
- Produces: installable manifest, versioned app-shell caching, stale-while-revalidate static assets, and automated browser acceptance coverage.

- [ ] **Step 1: Write failing Playwright acceptance tests**

Create tests that publish and serve the app, load it once online, import a route, set the browser context offline, reload `/routes`, verify the route remains, start a mocked-geolocation ride, push two positions, stop, reload `/rides`, and verify persistence. Add invocation success/failure tests with a local signed-payload server and URL cleanup assertion.

- [ ] **Step 2: Run the E2E tests to verify failure**

Run:

```bash
dotnet build src/RoutePacer.App/RoutePacer.App.csproj -c Release
dotnet test tests/RoutePacer.E2E --filter "FullyQualifiedName~OfflinePwaTests|FullyQualifiedName~TrackingCapabilityTests|FullyQualifiedName~RouteTimerInvocationTests"
```

Expected: FAIL until cache policy, icons, and browser harness are complete.

- [ ] **Step 3: Configure the installable manifest**

Set `name` to `RoutePacer`, `short_name` to `RoutePacer`, `start_url` to `/`, `scope` to `/`, `display` to `standalone`, portrait orientation, theme/background colors with tracker contrast, and 192/512 maskable icons. Keep `/open` as an ordinary navigation route; do not register a share target in MVP.

- [ ] **Step 4: Implement safe cache upgrades**

Use a versioned cache prefix, precache the generated Blazor asset manifest, delete only old RoutePacer caches on activate, serve navigation from cached `index.html` on network failure, and use stale-while-revalidate for same-origin static GET assets. Never cache `/open` query strings, RouteTimer payload responses, or non-GET requests.

- [ ] **Step 5: Add failure and capability browser cases**

Verify geolocation denied shows recovery guidance, missing Wake Lock shows a non-blocking notice, wake lock reacquires after hidden→visible, payload expiry/tamper produces manual-import fallback, and a timing-free route hides time delta while tracking continues.

- [ ] **Step 6: Write the physical-device validation matrix**

In `docs/manual-validation.md`, record exact steps and pass/fail spaces for:

- iOS Safari installed PWA: first load, offline relaunch, GPS permission, 60-minute active-screen ride;
- Android Chrome installed PWA: same plus Wake Lock acquire/revoke/reacquire;
- desktop Chrome/Edge: IndexedDB upgrade, offline reload, RouteTimer deep link;
- large 250,000-point route: import time, memory, first match, 60-minute persistence;
- airplane mode: routes/history available and a previously imported route starts tracking.

- [ ] **Step 7: Run the release acceptance suite**

Run:

```bash
dotnet publish src/RoutePacer.App/RoutePacer.App.csproj -c Release
dotnet test RoutePacer.slnx -c Release --no-restore
```

Expected: all unit, component, and browser tests pass; the published app contains manifest, icons, service worker, WASM assets, and no signing secret.

- [ ] **Step 8: Commit**

```bash
git add src/RoutePacer.App/wwwroot tests/RoutePacer.E2E docs/manual-validation.md
git commit -m "test: verify installable offline RoutePacer PWA"
```

### Task 16: Add Performance Regression Coverage and Release Documentation

**Files:**
- Create: `tests/RoutePacer.Core.Tests/Performance/RouteMatcherPerformanceTests.cs`
- Create: `tests/RoutePacer.App.Tests/Rides/LongRideStabilityTests.cs`
- Modify: `README.md`
- Create: `docs/architecture.md`
- Create: `docs/privacy.md`
- Create: `docs/route-timer-rollout.md`

**Interfaces:**
- Consumes: completed application and all prior public interfaces.
- Produces: measurable performance gates, operational docs, privacy behavior, and cross-app rollout sequence.

- [ ] **Step 1: Write deterministic performance regression tests**

Generate a 250,000-point synthetic route in memory, warm the matcher, then assert 1,000 windowed matches complete within 2 seconds on the test runner while allocating less than 25 MB for the measured loop. Simulate 21,600 GPS fixes (one per second for six hours) and assert every accepted point is persisted, sequence remains monotonic, and published UI snapshots are at most four per second.

- [ ] **Step 2: Run performance tests and capture baseline**

Run:

```bash
dotnet test tests/RoutePacer.Core.Tests --filter FullyQualifiedName~RouteMatcherPerformanceTests -c Release
dotnet test tests/RoutePacer.App.Tests --filter FullyQualifiedName~LongRideStabilityTests -c Release
```

Expected: PASS on the development machine. If CI hardware differs, preserve the workload and set a separately measured CI threshold rather than deleting the gate.

- [ ] **Step 3: Document architecture and privacy**

`docs/architecture.md` must describe project boundaries, import normalization, IndexedDB schema/versioning, match/pacing formulas and signs, session transitions, and offline cache exclusions. `docs/privacy.md` must state that location/routes/rides remain on-device, list browser permissions, describe delete actions, and state that RouteTimer payload bytes are fetched only during explicit handoff.

- [ ] **Step 4: Document the coordinated RouteTimer rollout**

Sequence deployment as: publish RoutePacer with RouteTimer button disabled; publish the matching RouteTimer payload endpoint and ECDSA signer; run the shared valid/tampered fixtures against production-like origins; enable the button; monitor only aggregate server response codes without route/token contents. Explicitly note that RouteTimer's earlier HMAC plan must be updated to ECDSA P-256 before production enablement.

- [ ] **Step 5: Run final repository checks**

Run:

```bash
rg -n "SigningKey|HMACSHA|private key" src/RoutePacer.App/wwwroot
dotnet format RoutePacer.slnx --verify-no-changes
dotnet build RoutePacer.slnx -c Release
dotnet test RoutePacer.slnx -c Release --no-build
git status --short
```

Expected: the secret scan returns no matches; formatting, build, and tests pass; status contains only intentional documentation or generated Playwright artifacts that are either committed deliberately or ignored.

- [ ] **Step 6: Commit**

```bash
git add tests README.md docs/architecture.md docs/privacy.md docs/route-timer-rollout.md
git commit -m "docs: complete RoutePacer release guidance"
```

---

## MVP Acceptance Traceability

| Source requirement | Implemented and proven by |
|---|---|
| Installable PWA and offline startup | Tasks 1 and 15 |
| GPX and FIT import | Tasks 3–5 and 7 |
| IndexedDB route/ride persistence | Tasks 6, 7, 12, and 14 |
| Select route and start tracking | Tasks 7, 12, and 13 |
| High-accuracy active-screen GPS | Tasks 11–13 |
| Route projection and position | Task 9 |
| Distance and time lead/lag | Tasks 10 and 13 |
| Best-effort Wake Lock and recovery | Tasks 11, 13, and 15 |
| Ride history available offline | Tasks 12, 14, and 15 |
| RouteTimer auto-import-to-ready flow | Tasks 8 and 15 |
| Distance-only fallback for untimed routes | Tasks 3, 10, 13, and 15 |
| Local-only privacy and deletion | Tasks 6, 14, and 16 |
| Large-route and long-ride stability | Tasks 9, 12, 15, and 16 |

## Deferred Enhancement

Web Share Target support remains outside MVP. After Contract v1 is deployed and stable, add a separate design and plan covering manifest `share_target`, service-worker multipart intake, temporary Cache Storage transfer, MIME/size validation, duplicate-import prevention, and iOS/Android support testing.
