# Offline-First RoutePacer Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build an installable Blazor WebAssembly PWA that imports timed GPX/FIT routes, tracks an active-screen ride entirely offline, shows stable time and distance lead/lag, persists rides locally, and accepts a secure RouteTimer handoff.

**Architecture:** Use a dependency-free `RoutePacer.Core` library for route and ride behavior, a `RoutePacer.App` Blazor WebAssembly PWA for IndexedDB and offline tracking, a `RoutePacer.Server` ASP.NET Core host for the PWA and public relay API, and `RoutePacer.Persistence` for the dedicated PostgreSQL handoff store. RouteTimer uploads timed GPX outbound to the same-origin relay; the phone verifies Contract v1, atomically consumes the payload once, imports it through the same client pipeline as manual GPX, and keeps the resulting route on-device.

**Tech Stack:** .NET 10, C# 14, ASP.NET Core minimal APIs, Blazor WebAssembly PWA, EF Core 10, Npgsql, PostgreSQL 16, Docker Compose, Caddy, `Garmin.FIT.Sdk`, `System.Xml.Linq`, browser IndexedDB, Web Crypto, Geolocation API, Screen Wake Lock API, xUnit, FluentAssertions, bUnit, Testcontainers, and Microsoft Playwright.

**Spec:** `docs/superpowers/specs/2026-08-27-routepacer-public-handoff-relay-design.md`, reconciling the original product brief in `OFFLINE_FIRST_BLAZOR_CYCLING_APP_PLAN.md`

## Global Constraints

- Tracking targets an active, visible screen; continuous background tracking and native background services remain out of scope.
- The app must start without a network connection after its first successful load/install.
- Manual imports, imported routes, ride data, and tracking remain client-side in IndexedDB; an explicit RouteTimer handoff temporarily stores readable GPX in the relay for no more than 10 minutes and deletes it immediately on successful consumption.
- Manual import accepts only `.gpx` and `.fit` files, with at least 3 valid points and a maximum file size of 50 MB.
- Geolocation permission is requested only after the rider explicitly starts a ride.
- GPS uses `enableHighAccuracy: true`, `timeout: 5000`, and `maximumAge: 0`.
- Wake lock is best effort, requires a secure context, and must recover after visibility returns when a ride is active.
- Time delta uses `DeltaTimeSeconds = live elapsed time - target elapsed time`; negative means ahead and positive means behind.
- Distance delta uses route-progress semantics: projected rider distance minus expected route distance at the same live elapsed time. Cross-track error is displayed separately.
- A route without usable timing remains trackable in distance-only mode and never fabricates a time delta.
- RouteTimer Contract v1 requires `src`, `v`, `payload`, `name`, `ts`, and `sig` exactly once; canonical bytes are `rt\n1\n{payload-absolute-uri}\n{name-or-empty}\n{unix-milliseconds}` with UTF-8 encoding and no trailing line feed.
- Contract v1 uses ECDSA P-256, SHA-256, and fixed-width IEEE-P1363 signatures; RoutePacer publishes only RouteTimer's configured public JWK.
- Relay uploads accept only a non-empty `application/gpx+xml` body of at most 52,428,800 bytes and fix expiry at exactly 10 minutes.
- The relay generates a 32-byte random token, returns 43-character unpadded base64url, and stores only `SHA-256(token)`.
- Consumption is one PostgreSQL `DELETE ... RETURNING` operation: the first unexpired request returns exact bytes and deletes the row immediately; every other outcome is the same `404`.
- The dedicated PostgreSQL database has restart-durable storage, publishes no host port, and has no backup or restore path.
- Relay uploads and PWA RouteTimer intake are independently disableable and tracked production configuration keeps both disabled.
- Application, Caddy, ingress, trace, and metric output never contains credentials, tokens, payload URLs, invocation queries, signatures, route names, or GPX bytes.
- The RoutePacer production origin is `https://pacetracking.tqaentry.com`.
- Web Share Target intake is an enhancement after the v1 HTTPS deep-link path, not an MVP prerequisite.
- Production deployment is forward-only and follows RouteTimer's Docker Compose plus shared-Caddy pattern; there is no rollback plan.

---

## File and Responsibility Map

| Area | Files | Responsibility |
|---|---|---|
| Solution | `RoutePacer.slnx`, `global.json`, `Directory.Build.props`, `Directory.Packages.props` | Pin .NET 10, package versions, and shared build/test policy. |
| Core domain | `src/RoutePacer.Core/Domain/*.cs` | Immutable route, route-point, ride, location, pacing, and state types. |
| Import | `src/RoutePacer.Core/Import/*.cs` | GPX/FIT parsing, validation, normalization, cumulative distance, and timing. |
| Matching/pacing | `src/RoutePacer.Core/Tracking/*.cs` | Metric projection, segment-window matching, temporal interpolation, and lead/lag math. |
| Hosted server | `src/RoutePacer.Server/{Program.cs,Hosting,Health,Handoffs}/*` | Serve the PWA, relay API, feature controls, safe logging, rate limiting, health, and SPA fallback. |
| Relay persistence | `src/RoutePacer.Persistence/Handoffs/*`, `src/RoutePacer.Persistence/Migrations/*` | PostgreSQL token hashing, exact payload storage, atomic delete-returning consumption, migrations, and expiry cleanup. |
| Browser persistence | `src/RoutePacer.App/Storage/*.cs`, `src/RoutePacer.App/wwwroot/js/storage.js` | Versioned IndexedDB schema and typed transactional access. |
| Browser capabilities | `src/RoutePacer.App/Browser/*.cs`, `src/RoutePacer.App/wwwroot/js/{gps,wakelock,invocation}.js` | GPS callbacks, wake lock, strict URL intake/cleanup, and P-256 verification. |
| Application workflows | `src/RoutePacer.App/Routes/*.cs`, `src/RoutePacer.App/Rides/*.cs`, `src/RoutePacer.App/Invocation/*.cs` | Import, library, tracking state machine, ride recording, and RouteTimer handoff. |
| UI | `src/RoutePacer.App/Pages/*.razor`, `src/RoutePacer.App/Components/*.razor` | Import, library, tracker, history, status, and failure states. |
| Offline shell | `src/RoutePacer.App/wwwroot/{manifest.webmanifest,service-worker.js,service-worker.published.js}` | Installability, cache versioning, app-shell offline behavior. |
| Deployment | `Dockerfile`, `deploy/{docker-compose.yml,docker-compose.local.yml,.env.example,README.md,caddy/routepacer.caddy}` | Container build, dedicated PostgreSQL, Caddy routing, secrets, forward-only deployment, and smoke procedure. |
| Tests | `tests/RoutePacer.Core.Tests`, `tests/RoutePacer.App.Tests`, `tests/RoutePacer.Server.Tests`, `tests/RoutePacer.Persistence.Tests`, `tests/RoutePacer.E2E` | Unit, bUnit, real PostgreSQL/API, deployment, and browser acceptance coverage. |

The implementation is intentionally split into tasks that leave a reviewable, testable capability. Do not combine later UI work into earlier domain tasks.

### Task 1: Scaffold the .NET 10 PWA and Test Boundaries

**Files:**
- Create: `global.json`
- Create: `Directory.Build.props`
- Create: `Directory.Packages.props`
- Create: `.config/dotnet-tools.json`
- Create: `RoutePacer.slnx`
- Create: `src/RoutePacer.Core/RoutePacer.Core.csproj`
- Create: `src/RoutePacer.App/RoutePacer.App.csproj`
- Create: `src/RoutePacer.App/Program.cs`
- Create: `src/RoutePacer.Server/RoutePacer.Server.csproj`
- Create: `src/RoutePacer.Server/Program.cs`
- Create: `src/RoutePacer.Persistence/RoutePacer.Persistence.csproj`
- Create: `tests/RoutePacer.Core.Tests/RoutePacer.Core.Tests.csproj`
- Create: `tests/RoutePacer.App.Tests/RoutePacer.App.Tests.csproj`
- Create: `tests/RoutePacer.Server.Tests/RoutePacer.Server.Tests.csproj`
- Create: `tests/RoutePacer.Persistence.Tests/RoutePacer.Persistence.Tests.csproj`
- Create: `tests/RoutePacer.E2E/RoutePacer.E2E.csproj`
- Modify: `README.md`

**Interfaces:**
- Consumes: none.
- Produces: solution projects targeting `net10.0`; `RoutePacer.App` references `RoutePacer.Core`; `RoutePacer.Server` hosts the published app and references `RoutePacer.Persistence`; each test project references its production target.

- [ ] **Step 1: Pin the SDK and create the solution skeleton**

Run:

```bash
dotnet new globaljson --sdk-version 10.0.302 --roll-forward latestFeature
dotnet new tool-manifest
dotnet tool install dotnet-ef --version 10.0.11
dotnet new sln --name RoutePacer --format slnx
dotnet new classlib --name RoutePacer.Core --output src/RoutePacer.Core --framework net10.0
dotnet new blazorwasm --name RoutePacer.App --output src/RoutePacer.App --framework net10.0 --pwa --no-https
dotnet new webapi --name RoutePacer.Server --output src/RoutePacer.Server --framework net10.0 --no-openapi
dotnet new classlib --name RoutePacer.Persistence --output src/RoutePacer.Persistence --framework net10.0
dotnet new xunit --name RoutePacer.Core.Tests --output tests/RoutePacer.Core.Tests --framework net10.0
dotnet new xunit --name RoutePacer.App.Tests --output tests/RoutePacer.App.Tests --framework net10.0
dotnet new xunit --name RoutePacer.Server.Tests --output tests/RoutePacer.Server.Tests --framework net10.0
dotnet new xunit --name RoutePacer.Persistence.Tests --output tests/RoutePacer.Persistence.Tests --framework net10.0
dotnet new xunit --name RoutePacer.E2E --output tests/RoutePacer.E2E --framework net10.0
```

Expected: all twelve commands exit `0`; the tool manifest pins EF CLI 10.0.11 and the PWA template creates `manifest.webmanifest` plus both service-worker files.

- [ ] **Step 2: Add projects and references**

Run:

```bash
dotnet sln RoutePacer.slnx add src/RoutePacer.Core/RoutePacer.Core.csproj src/RoutePacer.App/RoutePacer.App.csproj src/RoutePacer.Server/RoutePacer.Server.csproj src/RoutePacer.Persistence/RoutePacer.Persistence.csproj tests/RoutePacer.Core.Tests/RoutePacer.Core.Tests.csproj tests/RoutePacer.App.Tests/RoutePacer.App.Tests.csproj tests/RoutePacer.Server.Tests/RoutePacer.Server.Tests.csproj tests/RoutePacer.Persistence.Tests/RoutePacer.Persistence.Tests.csproj tests/RoutePacer.E2E/RoutePacer.E2E.csproj
dotnet add src/RoutePacer.App/RoutePacer.App.csproj reference src/RoutePacer.Core/RoutePacer.Core.csproj
dotnet add src/RoutePacer.Persistence/RoutePacer.Persistence.csproj reference src/RoutePacer.Core/RoutePacer.Core.csproj
dotnet add src/RoutePacer.Server/RoutePacer.Server.csproj reference src/RoutePacer.Persistence/RoutePacer.Persistence.csproj
dotnet add tests/RoutePacer.Core.Tests/RoutePacer.Core.Tests.csproj reference src/RoutePacer.Core/RoutePacer.Core.csproj
dotnet add tests/RoutePacer.App.Tests/RoutePacer.App.Tests.csproj reference src/RoutePacer.App/RoutePacer.App.csproj
dotnet add tests/RoutePacer.Server.Tests/RoutePacer.Server.Tests.csproj reference src/RoutePacer.Server/RoutePacer.Server.csproj
dotnet add tests/RoutePacer.Persistence.Tests/RoutePacer.Persistence.Tests.csproj reference src/RoutePacer.Persistence/RoutePacer.Persistence.csproj
```

- [ ] **Step 3: Pin dependency versions centrally**

Create `Directory.Packages.props` with central management and these reviewed pins:

```xml
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
  </PropertyGroup>
  <ItemGroup>
    <PackageVersion Include="coverlet.collector" Version="6.0.4" />
    <PackageVersion Include="bunit" Version="2.9.0" />
    <PackageVersion Include="Garmin.FIT.Sdk" Version="21.213.0" />
    <PackageVersion Include="FluentAssertions" Version="8.10.0" />
    <PackageVersion Include="Microsoft.AspNetCore.Components.WebAssembly.Server" Version="10.0.11" />
    <PackageVersion Include="Microsoft.AspNetCore.Components.WebAssembly" Version="10.0.11" />
    <PackageVersion Include="Microsoft.AspNetCore.Components.WebAssembly.DevServer" Version="10.0.11" />
    <PackageVersion Include="Microsoft.AspNetCore.Mvc.Testing" Version="10.0.11" />
    <PackageVersion Include="Microsoft.EntityFrameworkCore.Design" Version="10.0.11" />
    <PackageVersion Include="Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore" Version="10.0.11" />
    <PackageVersion Include="Microsoft.Extensions.TimeProvider.Testing" Version="10.9.0" />
    <PackageVersion Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
    <PackageVersion Include="Microsoft.Playwright.Xunit" Version="1.62.0" />
    <PackageVersion Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="10.0.3" />
    <PackageVersion Include="Testcontainers.PostgreSql" Version="4.14.0" />
    <PackageVersion Include="xunit" Version="2.9.3" />
    <PackageVersion Include="xunit.runner.visualstudio" Version="3.1.4" />
  </ItemGroup>
</Project>
```

Add package references without project-local versions:

```bash
dotnet add src/RoutePacer.Core/RoutePacer.Core.csproj package Garmin.FIT.Sdk
dotnet add src/RoutePacer.Persistence/RoutePacer.Persistence.csproj package Npgsql.EntityFrameworkCore.PostgreSQL
dotnet add src/RoutePacer.Server/RoutePacer.Server.csproj package Microsoft.AspNetCore.Components.WebAssembly.Server
dotnet add src/RoutePacer.Server/RoutePacer.Server.csproj package Microsoft.EntityFrameworkCore.Design
dotnet add src/RoutePacer.Server/RoutePacer.Server.csproj package Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore
dotnet add tests/RoutePacer.Core.Tests/RoutePacer.Core.Tests.csproj package FluentAssertions
dotnet add tests/RoutePacer.App.Tests/RoutePacer.App.Tests.csproj package bunit
dotnet add tests/RoutePacer.App.Tests/RoutePacer.App.Tests.csproj package FluentAssertions
dotnet add tests/RoutePacer.Server.Tests/RoutePacer.Server.Tests.csproj package Microsoft.AspNetCore.Mvc.Testing
dotnet add tests/RoutePacer.Server.Tests/RoutePacer.Server.Tests.csproj package Microsoft.Extensions.TimeProvider.Testing
dotnet add tests/RoutePacer.Persistence.Tests/RoutePacer.Persistence.Tests.csproj package Testcontainers.PostgreSql
dotnet add tests/RoutePacer.Persistence.Tests/RoutePacer.Persistence.Tests.csproj package Microsoft.Extensions.TimeProvider.Testing
dotnet add tests/RoutePacer.E2E/RoutePacer.E2E.csproj package Microsoft.Playwright.Xunit
```

Remove project-local `Version` attributes emitted by templates so every package resolves through `Directory.Packages.props`. All test projects retain centrally managed `Microsoft.NET.Test.Sdk`, `xunit`, and `xunit.runner.visualstudio` references.

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

Delete the Web API sample endpoint, update the app title/navigation to `RoutePacer`, and remove the template counter/weather pages. Add this static-web-asset reference to `RoutePacer.Server.csproj`:

```xml
<ItemGroup>
  <ProjectReference Include="..\RoutePacer.App\RoutePacer.App.csproj"
                    ReferenceOutputAssembly="false" />
</ItemGroup>
```

Configure the server with `UseBlazorFrameworkFiles()` and `UseStaticFiles()`. Its final fallback returns `404` for paths beginning `/api` or `/health`; every other unmapped navigation sends `wwwroot/index.html`. This prevents misspelled API and health URLs from receiving the SPA shell.

Add these commands to `README.md`:

```bash
dotnet restore RoutePacer.slnx
dotnet build RoutePacer.slnx --no-restore
dotnet test RoutePacer.slnx --no-build
dotnet run --project src/RoutePacer.Server/RoutePacer.Server.csproj
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
git add global.json Directory.Build.props Directory.Packages.props .config/dotnet-tools.json RoutePacer.slnx src tests README.md
git commit -m "build: scaffold hosted RoutePacer solution"
git push -u origin HEAD
```

### Review Gate 1: Scaffold and Boundaries

- [ ] Stop before Task 2 and confirm the Task 1 restore, build, and test commands pass from a clean checkout of the pushed branch.
- [ ] Review the solution structure, dependency pins, project references, strict build settings, hosted-PWA fallback behavior, and removal of template features against Task 1 and the global constraints.
- [ ] Record every finding, implement corrections, and rerun the complete Task 1 verification.
- [ ] Stage only Task 1 correction files, commit, and push:

```bash
git add global.json Directory.Build.props Directory.Packages.props .config/dotnet-tools.json RoutePacer.slnx src tests README.md
git commit -m "fix: address scaffold review"
git push -u origin HEAD
```

- [ ] Continue to Task 2 only after the reviewer explicitly approves this gate.

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
git push -u origin HEAD
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
git push -u origin HEAD
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
git push -u origin HEAD
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
- Consumes: `Garmin.FIT.Sdk`, `IRouteFileParser`, `RouteNormalizer`.
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

Subscribe to `RecordMesg` from the Garmin FIT decoder, accept records only when both position fields are present, convert FIT semicircles with `degrees = semicircles * (180d / 2147483648d)`, and capture altitude, timestamp, and elapsed-time fields when available. Map decoder failures to `RouteImportException("malformed-fit", ...)` and enforce the same 250,000-point ceiling as GPX.

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
git push -u origin HEAD
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
git push -u origin HEAD
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
git push -u origin HEAD
```

### Review Gate 2: Manual Import and Offline Route Library

- [ ] Stop before Task 8 and run `dotnet test RoutePacer.slnx` followed by `dotnet build RoutePacer.slnx`; both commands must pass with zero warnings.
- [ ] Review Tasks 2–7 for domain invariants, GPX/FIT parser safety, normalization correctness, transactional IndexedDB behavior, the 50 MB limit, actionable import failures, and the complete manual-import-to-Start-ride workflow.
- [ ] Exercise the manual picker with representative timed GPX, untimed GPX, FIT, malformed, empty, and oversized inputs; confirm failed imports leave no partial route data.
- [ ] Record every finding, implement corrections, and rerun the full solution tests and build.
- [ ] Stage only corrections within the Tasks 2–7 scope, commit, and push:

```bash
git add src/RoutePacer.Core src/RoutePacer.App tests/RoutePacer.Core.Tests tests/RoutePacer.App.Tests
git commit -m "fix: address import workflow review"
git push -u origin HEAD
```

- [ ] Continue to Task 8 only after the reviewer explicitly approves this gate.

### Task 8: Create the Dedicated Handoff Store

**Files:**
- Create: `src/RoutePacer.Persistence/Handoffs/HandoffRecord.cs`
- Create: `src/RoutePacer.Persistence/Handoffs/IHandoffStore.cs`
- Create: `src/RoutePacer.Persistence/Handoffs/PostgresHandoffStore.cs`
- Create: `src/RoutePacer.Persistence/Handoffs/HandoffToken.cs`
- Create: `src/RoutePacer.Persistence/RoutePacerDbContext.cs`
- Create: `src/RoutePacer.Persistence/Migrations/<timestamp>_CreateHandoffs.cs`
- Create: `tests/RoutePacer.Persistence.Tests/Handoffs/HandoffTokenTests.cs`
- Create: `tests/RoutePacer.Persistence.Tests/Handoffs/PostgresHandoffStoreTests.cs`
- Create: `tests/RoutePacer.Persistence.Tests/DatabaseFixture.cs`

**Interfaces:**
- Consumes: Npgsql EF Core, PostgreSQL 16, `TimeProvider`.
- Produces: `HandoffToken.Create()`, `HandoffToken.Hash(string)`, and `IHandoffStore.InsertAsync`, `ConsumeAsync`, and `DeleteExpiredAsync`.

- [ ] **Step 1: Write failing token and schema tests**

```csharp
[Fact]
public void Create_returns_43_character_unpadded_base64url_and_32_byte_hash()
{
    var token = HandoffToken.Create();

    Assert.Matches("^[A-Za-z0-9_-]{43}$", token.Plaintext);
    Assert.Equal(32, token.Sha256.Length);
    Assert.Equal(SHA256.HashData(Base64Url.Decode(token.Plaintext)), token.Sha256);
}

[Fact]
public async Task Migration_creates_only_the_approved_handoff_columns()
{
    var columns = await Database.QueryColumnsAsync("handoffs");
    Assert.Equal(["token_hash", "content", "created_at", "expires_at"], columns);
}
```

Also assert `token_hash` is the primary key, `content` is `bytea`, timestamps are `timestamptz`, and no token, URL, name, or consumed column exists.

- [ ] **Step 2: Run the focused tests to verify failure**

Run: `dotnet test tests/RoutePacer.Persistence.Tests --filter "FullyQualifiedName~HandoffTokenTests|FullyQualifiedName~PostgresHandoffStoreTests"`

Expected: FAIL because the persistence types and migration do not exist.

- [ ] **Step 3: Define the store contract and token helper**

```csharp
public interface IHandoffStore
{
    Task InsertAsync(byte[] tokenHash, ReadOnlyMemory<byte> content,
        DateTimeOffset createdAt, DateTimeOffset expiresAt,
        CancellationToken cancellationToken = default);
    Task<byte[]?> ConsumeAsync(byte[] tokenHash, DateTimeOffset now,
        CancellationToken cancellationToken = default);
    Task<int> DeleteExpiredAsync(DateTimeOffset now,
        CancellationToken cancellationToken = default);
}

public sealed record HandoffToken(string Plaintext, byte[] Sha256)
{
    public static HandoffToken Create();
    public static byte[] Hash(string plaintext);
}
```

`Create` fills exactly 32 bytes with `RandomNumberGenerator.Fill`, returns unpadded base64url, and hashes the decoded random bytes. `Hash` rejects anything outside the exact 43-character base64url shape before decoding and hashing.

- [ ] **Step 4: Implement the minimal EF model and migration**

Map `HandoffRecord` to `handoffs` with the four approved columns. Do not add a navigation, generated numeric ID, concurrency token, consumption flag, or audit columns. Generate the migration with:

```bash
dotnet tool run dotnet-ef migrations add CreateHandoffs --project src/RoutePacer.Persistence --startup-project src/RoutePacer.Server
```

- [ ] **Step 5: Implement atomic persistence operations**

Use parameterized SQL for consumption:

```sql
DELETE FROM handoffs
WHERE token_hash = @token_hash AND expires_at > @now
RETURNING content;
```

`InsertAsync` copies the exact byte sequence. `DeleteExpiredAsync` uses `DELETE FROM handoffs WHERE expires_at <= @now`. Neither operation logs parameters or entity values.

- [ ] **Step 6: Run migration and store tests**

Run:

```bash
dotnet test tests/RoutePacer.Persistence.Tests --filter "FullyQualifiedName~HandoffTokenTests|FullyQualifiedName~PostgresHandoffStoreTests"
dotnet tool run dotnet-ef migrations has-pending-model-changes --project src/RoutePacer.Persistence --startup-project src/RoutePacer.Server
```

Expected: tests PASS; pending-model command reports no changes.

- [ ] **Step 7: Commit**

```bash
git add src/RoutePacer.Persistence tests/RoutePacer.Persistence.Tests
git commit -m "feat: add ephemeral PostgreSQL handoff store"
git push -u origin HEAD
```

### Task 9: Implement Authenticated Relay Creation

**Files:**
- Create: `src/RoutePacer.Server/Handoffs/HandoffRelayOptions.cs`
- Create: `src/RoutePacer.Server/Handoffs/HandoffEndpoints.cs`
- Create: `src/RoutePacer.Server/Handoffs/HandoffUploadService.cs`
- Create: `src/RoutePacer.Server/Handoffs/UploadCredentialVerifier.cs`
- Create: `src/RoutePacer.Server/Handoffs/LimitedRequestBodyReader.cs`
- Create: `src/RoutePacer.Server/Handoffs/HandoffCreatedResponse.cs`
- Create: `src/RoutePacer.Server/Hosting/SensitiveRequestLoggingFilter.cs`
- Modify: `src/RoutePacer.Server/Program.cs`
- Create: `tests/RoutePacer.Server.Tests/Handoffs/HandoffCreationTests.cs`
- Create: `tests/RoutePacer.Server.Tests/Handoffs/UploadCredentialVerifierTests.cs`
- Create: `tests/RoutePacer.Server.Tests/Handoffs/LimitedRequestBodyReaderTests.cs`

**Interfaces:**
- Consumes: `IHandoffStore`, `HandoffToken`, `TimeProvider`, ASP.NET Core rate limiting.
- Produces: authenticated `POST /api/handoffs`, `HandoffCreatedResponse(string PayloadUrl, DateTimeOffset ExpiresAt)`, and redacted request logging.

- [ ] **Step 1: Write failing API contract tests**

Use `WebApplicationFactory<Program>` with a recording `IHandoffStore` and fake `TimeProvider`. Cover exact GPX bytes and `201`, missing/invalid bearer `401`, empty body `400`, 52,428,801 bytes `413`, any media type other than exactly `application/gpx+xml` as `415`, rate limit `429`, disabled uploads `503`, and store failure as a safe `500`.

```csharp
[Fact]
public async Task Valid_upload_returns_exact_origin_and_ten_minute_expiry()
{
    Clock.SetUtcNow(new DateTimeOffset(2026, 8, 27, 12, 0, 0, TimeSpan.Zero));
    using var request = GpxUpload("<gpx/>", ValidCredential);

    var response = await Client.SendAsync(request);
    var body = await response.Content.ReadFromJsonAsync<HandoffCreatedResponse>();

    Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    Assert.Matches("^https://pacetracking\\.tqaentry\\.com/api/handoffs/[A-Za-z0-9_-]{43}$", body!.PayloadUrl);
    Assert.Equal(Clock.GetUtcNow().AddMinutes(10), body.ExpiresAt);
    Assert.Equal("no-store", response.Headers.CacheControl!.ToString());
}
```

- [ ] **Step 2: Run creation tests to verify failure**

Run: `dotnet test tests/RoutePacer.Server.Tests --filter FullyQualifiedName~HandoffCreationTests`

Expected: FAIL because the endpoint is not mapped.

- [ ] **Step 3: Implement constant-time bearer verification**

```csharp
public sealed class UploadCredentialVerifier(IOptions<HandoffRelayOptions> options)
{
    public bool IsValid(string? authorizationHeader);
}
```

Require one `Bearer` value. Hash the UTF-8 bytes of both the presented credential and configured credential with SHA-256, then compare the fixed-size digests with `CryptographicOperations.FixedTimeEquals`. Tests cover malformed schemes, duplicates, empty values, different lengths, and exact matches.

- [ ] **Step 4: Implement bounded upload reading**

`LimitedRequestBodyReader.ReadAsync(Stream, 52_428_800, cancellationToken)` reads in pooled chunks, rejects zero bytes, throws `PayloadTooLargeException` as soon as byte 52,428,801 is observed, and returns the exact bytes. Tests use streams with absent, false-small, exact, and false-large lengths; request `Content-Length` above the maximum is rejected before reading.

- [ ] **Step 5: Map creation with fixed contract and rate limiting**

Bind:

```csharp
public sealed class HandoffRelayOptions
{
    public bool UploadsEnabled { get; init; }
    public required Uri PublicOrigin { get; init; }
    public string UploadCredential { get; init; } = "";
    public int MaximumUploadBytes { get; init; } = 52_428_800;
    public TimeSpan Lifetime { get; init; } = TimeSpan.FromMinutes(10);
}
```

Startup validation requires `PublicOrigin` to equal `https://pacetracking.tqaentry.com` and contain only an origin. It requires a credential only when enabled and refuses configured maximum or lifetime values that differ from the frozen constants. Apply a named global fixed-window policy of 10 upload attempts per minute with zero queued requests; exhausted permits return `429`. Authenticate, rate-limit, and validate media type before reading. Create the token, capture `now` once, insert the hash/content with `expiresAt = now.AddMinutes(10)`, and return the absolute payload URL with `Cache-Control: no-store`.

- [ ] **Step 6: Suppress sensitive request data before logging**

Disable the framework request-start/request-finish and HTTP logging categories that emit raw request targets. Add one safe endpoint-aware middleware that records only method, the literal route template, status class, and aggregate byte count for `/api/handoffs`; it records only the literal `/open` path without a query. `Authorization` is never an allowed header. Never log the concrete GET path, raw request target, response body, exception parameters, token, hash, URL, name, signature, or GPX.

- [ ] **Step 7: Run focused and server tests**

Run:

```bash
dotnet test tests/RoutePacer.Server.Tests --filter "FullyQualifiedName~HandoffCreationTests|FullyQualifiedName~UploadCredentialVerifierTests|FullyQualifiedName~LimitedRequestBodyReaderTests"
dotnet test tests/RoutePacer.Server.Tests
```

Expected: PASS.

- [ ] **Step 8: Commit**

```bash
git add src/RoutePacer.Server tests/RoutePacer.Server.Tests
git commit -m "feat: accept authenticated GPX relay uploads"
git push -u origin HEAD
```

### Task 10: Implement One-Time Consumption and Expiry Cleanup

**Files:**
- Create: `src/RoutePacer.Server/Handoffs/HandoffCleanupService.cs`
- Modify: `src/RoutePacer.Server/Handoffs/HandoffEndpoints.cs`
- Modify: `src/RoutePacer.Server/Program.cs`
- Create: `tests/RoutePacer.Server.Tests/Handoffs/HandoffConsumptionTests.cs`
- Create: `tests/RoutePacer.Server.Tests/Handoffs/HandoffCleanupServiceTests.cs`
- Modify: `tests/RoutePacer.Persistence.Tests/Handoffs/PostgresHandoffStoreTests.cs`
- Create: `tests/RoutePacer.Persistence.Tests/Handoffs/HandoffReplicaTests.cs`

**Interfaces:**
- Consumes: `IHandoffStore.ConsumeAsync` and `DeleteExpiredAsync`, `TimeProvider`.
- Produces: anonymous `GET /api/handoffs/{token}` and periodic expired-row deletion.

- [ ] **Step 1: Write failing consumption header and safe-404 tests**

```csharp
[Fact]
public async Task First_get_returns_exact_bytes_and_required_headers_then_second_get_is_404()
{
    var token = await CreateStoredHandoffAsync("<gpx>exact</gpx>");

    var first = await Client.GetAsync($"/api/handoffs/{token}");
    var second = await Client.GetAsync($"/api/handoffs/{token}");

    Assert.Equal(HttpStatusCode.OK, first.StatusCode);
    Assert.Equal("<gpx>exact</gpx>", await first.Content.ReadAsStringAsync());
    Assert.Equal("application/gpx+xml", first.Content.Headers.ContentType!.MediaType);
    Assert.Equal(first.Content.Headers.ContentLength, (await first.Content.ReadAsByteArrayAsync()).Length);
    Assert.Equal("no-store", first.Headers.CacheControl!.ToString());
    Assert.Contains("no-cache", first.Headers.Pragma.ToString());
    Assert.Equal("nosniff", first.Headers.GetValues("X-Content-Type-Options").Single());
    Assert.Equal(HttpStatusCode.NotFound, second.StatusCode);
}
```

Assert malformed, unknown, expired, and consumed tokens have the same status, content length, media headers, and body.

- [ ] **Step 2: Write failing real-PostgreSQL concurrency and durability tests**

Start one PostgreSQL Testcontainer, construct two independent service providers/contexts, race two `ConsumeAsync` calls behind a barrier, and assert one exact byte array, one `null`, and zero rows. Restart the PostgreSQL container and prove an unexpired row remains consumable. Insert through one store instance and consume through another to prove replica sharing.

- [ ] **Step 3: Run tests to verify failure**

Run:

```bash
dotnet test tests/RoutePacer.Persistence.Tests --filter FullyQualifiedName~HandoffReplicaTests
dotnet test tests/RoutePacer.Server.Tests --filter FullyQualifiedName~HandoffConsumptionTests
```

Expected: FAIL because GET and cleanup are absent.

- [ ] **Step 4: Map anonymous atomic consumption**

Reject a token before repository access unless it matches `^[A-Za-z0-9_-]{43}$`. Hash the decoded 32 bytes, capture `now` once, and call `ConsumeAsync`. Return the successful exact byte array with the frozen headers. Return one shared empty `404` response for every other result. Add no cache, redirect, or caller identity behavior.

- [ ] **Step 5: Implement expiry cleanup**

`HandoffCleanupService` runs on startup and then at a one-minute maximum interval using `TimeProvider.CreateTimer`. Each iteration calls `DeleteExpiredAsync(now)` and records only the aggregate deleted-row count. Cancellation stops promptly. A cleanup failure logs only a fixed event ID and exception type, then retries on the next interval.

- [ ] **Step 6: Run concurrency, cleanup, and API tests**

Run:

```bash
dotnet test tests/RoutePacer.Persistence.Tests --filter "FullyQualifiedName~PostgresHandoffStoreTests|FullyQualifiedName~HandoffReplicaTests"
dotnet test tests/RoutePacer.Server.Tests --filter "FullyQualifiedName~HandoffConsumptionTests|FullyQualifiedName~HandoffCleanupServiceTests"
```

Expected: PASS; the concurrent case reports exactly one success on repeated runs.

- [ ] **Step 7: Commit**

```bash
git add src/RoutePacer.Server/Handoffs src/RoutePacer.Server/Program.cs tests/RoutePacer.Server.Tests tests/RoutePacer.Persistence.Tests
git commit -m "feat: consume relay handoffs exactly once"
git push -u origin HEAD
```

### Task 11: Implement RouteTimer Contract v1 Intake

**Files:**
- Create: `docs/contracts/route-timer-invocation-v1.md`
- Create: `docs/contracts/fixtures/route-timer-contract-v1.json`
- Create: `src/RoutePacer.App/Invocation/InvocationRequest.cs`
- Create: `src/RoutePacer.App/Invocation/InvocationParser.cs`
- Create: `src/RoutePacer.App/Invocation/InvocationCanonicalizer.cs`
- Create: `src/RoutePacer.App/Invocation/IInvocationVerifier.cs`
- Create: `src/RoutePacer.App/Invocation/WebCryptoInvocationVerifier.cs`
- Create: `src/RoutePacer.App/Invocation/BoundedReadStream.cs`
- Create: `src/RoutePacer.App/Invocation/HandoffPayloadClient.cs`
- Create: `src/RoutePacer.App/Invocation/IInvocationSettingsProvider.cs`
- Create: `src/RoutePacer.App/Invocation/ServerInvocationSettingsProvider.cs`
- Create: `src/RoutePacer.App/Invocation/RouteTimerInvocationService.cs`
- Create: `src/RoutePacer.App/Pages/Open.razor`
- Create: `src/RoutePacer.App/wwwroot/js/invocation.js`
- Modify: `src/RoutePacer.App/Program.cs`
- Create: `src/RoutePacer.Server/Configuration/RouteTimerInvocationOptions.cs`
- Create: `src/RoutePacer.Server/Configuration/ClientConfigurationEndpoints.cs`
- Modify: `src/RoutePacer.Server/Program.cs`
- Create: `tests/RoutePacer.App.Tests/Invocation/InvocationParserTests.cs`
- Create: `tests/RoutePacer.App.Tests/Invocation/InvocationFixtureTests.cs`
- Create: `tests/RoutePacer.App.Tests/Invocation/HandoffPayloadClientTests.cs`
- Create: `tests/RoutePacer.App.Tests/Invocation/RouteTimerInvocationServiceTests.cs`
- Create: `tests/RoutePacer.App.Tests/Pages/OpenTests.cs`
- Create: `tests/RoutePacer.Server.Tests/Configuration/ClientConfigurationEndpointTests.cs`

**Interfaces:**
- Consumes: `RouteCatalogService`, same-origin `HttpClient`, `TimeProvider`, server runtime configuration, browser Web Crypto and history APIs.
- Produces: public `GET /api/config/route-timer-invocation`, `InvocationRequest(Uri PayloadUri, string Name, long IssuedUnixMilliseconds, string Signature)`, `InvocationCanonicalizer.GetBytes`, `IInvocationVerifier.VerifyAsync`, `HandoffPayloadClient.FetchOnceAsync`, and the `/open` import-to-ready flow.

- [ ] **Step 1: Freeze the exact contract and shared fixture schema**

Document required query keys exactly once, exact origin/path rules, 10-minute past and 60-second future bounds, and these UTF-8 bytes with no trailing line feed:

```text
rt\n1\n{payload-absolute-uri}\n{name-or-empty}\n{unix-milliseconds}
```

The JSON fixture has exact properties `fixtureVersion`, `publicJwk`, `canonicalText`, `payloadUrl`, `name`, `timestamp`, `signature`, and `invocationUrl`. Generate one test-only P-256 key pair, record a fixed P1363 valid signature, derive tampered cases without altering the valid fixture, and copy the identical JSON bytes to RouteTimer's corresponding test fixture when its implementation task runs.

- [ ] **Step 2: Write failing strict parser and canonicalization tests**

Cover each missing and duplicate key, additional keys, wrong `src`/`v`, empty non-name fields, invalid percent escapes, invalid timestamp/signature encoding, more than 10 minutes old, exactly 10 minutes old, more than 60 seconds future, exactly 60 seconds future, HTTP, foreign origin, user info, query/fragment, wrong path, padded/wrong-length token, Unicode names, reserved characters, and empty name.

```csharp
[Fact]
public void Canonicalizer_has_line_feeds_between_fields_and_none_at_end()
{
    var bytes = InvocationCanonicalizer.GetBytes(Request(
        "https://pacetracking.tqaentry.com/api/handoffs/AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
        "Café & climb", 1787832000000));

    Assert.Equal("rt\n1\nhttps://pacetracking.tqaentry.com/api/handoffs/AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA\nCafé & climb\n1787832000000",
        Encoding.UTF8.GetString(bytes));
}
```

- [ ] **Step 3: Run parser tests to verify failure**

Run: `dotnet test tests/RoutePacer.App.Tests --filter "FullyQualifiedName~InvocationParserTests|FullyQualifiedName~InvocationFixtureTests"`

Expected: FAIL because parser, canonicalizer, and verifier are absent.

- [ ] **Step 4: Implement strict parse and Web Crypto verification**

Parse the raw query without collapsing duplicates. Require the exact six-key set, percent-decode once, and compare the normalized scheme, host, and effective port to `https://pacetracking.tqaentry.com`. In `invocation.js`, import only an EC JWK with `crv=P-256`, decode unpadded base64url P1363 signature bytes, and call `crypto.subtle.verify({ name: "ECDSA", hash: "SHA-256" }, key, signature, canonicalBytes)`. No private or symmetric material is accepted.

- [ ] **Step 5: Write failing media-type, bounded-read, and one-fetch tests**

Test missing/false-small/oversized `Content-Length`, exact 52,428,800-byte success, streamed byte 52,428,801 failure, any content type except exactly `application/gpx+xml`, non-success status, one GET on success, no automatic second GET after a response begins, exact byte preservation, and cancellation.

- [ ] **Step 6: Implement one-fetch payload client and orchestration**

Use `HttpCompletionOption.ResponseHeadersRead`, require `application/gpx+xml`, validate declared length, and wrap the response stream in `BoundedReadStream(52_428_800)`. `RouteTimerInvocationService` validates and verifies before GET, then calls the existing `RouteCatalogService.ImportAsync` as a `.gpx` source and waits for transactional IndexedDB persistence before reporting ready. Retry is permitted only for a failure proven to precede GET dispatch; after dispatch, return a terminal safe error requiring a new code or manual GPX.

- [ ] **Step 7: Implement `/open` states and immediate URL cleanup**

Render validating/downloading/importing progress, route name/distance plus `Start ride`, or `Could not import shared route` with safe recovery copy and manual file selection. Call `history.replaceState({}, "", "/open")` after terminal success or failure. Never render rejected values or log the query, name, signature, URL, token, host, or GPX.

- [ ] **Step 8: Register disabled-by-default runtime public configuration**

Bind server-side environment/configuration to:

```csharp
public sealed class RouteTimerInvocationOptions
{
    public bool Enabled { get; init; }
    public string PublicKeyJwk { get; init; } = "";
}
```

Tracked server settings default to disabled and an empty JWK. `GET /api/config/route-timer-invocation` returns only `{ "enabled": false }` while disabled or `{ "enabled": true, "publicKeyJwk": <object> }` while enabled, always with `Cache-Control: no-store`. Enabling intake with an absent, malformed, private (`d` present), non-EC, or non-P-256 JWK fails server startup. The client obtains this public configuration before verification. The origin, maximum age, future skew, route path, media type, and byte limit are code constants from the frozen contract, not mutable settings.

- [ ] **Step 9: Run invocation and component tests**

Run:

```bash
dotnet test tests/RoutePacer.App.Tests --filter FullyQualifiedName~Invocation
dotnet test tests/RoutePacer.App.Tests --filter FullyQualifiedName~OpenTests
dotnet test tests/RoutePacer.Server.Tests --filter FullyQualifiedName~ClientConfigurationEndpointTests
rg -n "PRIVATE KEY|RelayUpload|UploadCredential" src/RoutePacer.App/wwwroot
```

Expected: tests PASS; repository scan returns no matches.

- [ ] **Step 10: Commit**

```bash
git add docs/contracts src/RoutePacer.App/Invocation src/RoutePacer.App/Pages/Open.razor src/RoutePacer.App/wwwroot/js/invocation.js src/RoutePacer.App/Program.cs src/RoutePacer.Server/Configuration src/RoutePacer.Server/Program.cs tests/RoutePacer.App.Tests tests/RoutePacer.Server.Tests/Configuration
git commit -m "feat: import signed RouteTimer relay handoffs"
git push -u origin HEAD
```

### Review Gate 3: Relay and RouteTimer Contract v1

- [ ] Stop before Task 12 and run all Persistence, Server, App invocation, and `/open` tests plus `dotnet build RoutePacer.slnx`; every command must pass with zero warnings.
- [ ] Compare Tasks 8–11 line by line with `docs/superpowers/specs/2026-08-27-routepacer-public-handoff-relay-design.md`, including token shape and hashing, exact ten-minute expiry, atomic `DELETE ... RETURNING`, indistinguishable `404`s, strict media type and size limits, exact query parsing, P-256 verification, exact-origin allowlisting, one-fetch behavior, safe retry classification, and URL cleanup.
- [ ] Confirm the RoutePacer and RouteTimer Contract v1 fixture files are byte-identical and that no private key, upload credential, token, payload URL, route name, signature, invocation query, or GPX content appears in public assets or captured logs.
- [ ] Record every finding, implement corrections, and rerun the complete gate verification.
- [ ] Stage only corrections within the Tasks 8–11 scope, commit, and push:

```bash
git add docs/contracts src/RoutePacer.Persistence src/RoutePacer.Server src/RoutePacer.App/Invocation src/RoutePacer.App/Pages/Open.razor src/RoutePacer.App/wwwroot/js/invocation.js tests/RoutePacer.Persistence.Tests tests/RoutePacer.Server.Tests tests/RoutePacer.App.Tests
git commit -m "fix: address handoff contract review"
git push -u origin HEAD
```

- [ ] Continue to Task 12 only after the reviewer explicitly approves this gate.

### Task 12: Implement Spatial Route Matching

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
git push -u origin HEAD
```

### Task 13: Implement Time and Distance Pacing

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
git push -u origin HEAD
```

### Task 14: Bridge GPS and Wake Lock Browser Capabilities

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
git push -u origin HEAD
```

### Task 15: Record and Recover Ride Sessions

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
git push -u origin HEAD
```

### Task 16: Build the Live Tracker Dashboard

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
git push -u origin HEAD
```

### Task 17: Add Ride History, Detail, and Explicit Deletion

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
git push -u origin HEAD
```

### Review Gate 4: Pacing, Ride Lifecycle, and Rider UI

- [ ] Stop before Task 18 and run `dotnet test RoutePacer.slnx` followed by `dotnet build RoutePacer.slnx`; both commands must pass with zero warnings.
- [ ] Review Tasks 12–17 for projection accuracy, crossing stability, ahead/behind sign conventions, untimed-route behavior, GPS and Wake Lock lifecycle, spike rejection, pause/resume accounting, crash recovery, point persistence, UI throttling without data loss, accessible pacing presentation, ride history, and deletion constraints.
- [ ] Exercise the complete rider flow from a manually imported timed route through Start ride, live time and distance pacing, pause, resume, stop, history, detail, and deletion; repeat with an untimed route and verify distance-only behavior.
- [ ] Record every finding, implement corrections, and rerun the full solution tests and build.
- [ ] Stage only corrections within the Tasks 12–17 scope, commit, and push:

```bash
git add src/RoutePacer.Core/Tracking src/RoutePacer.App/Browser src/RoutePacer.App/Rides src/RoutePacer.App/Pages src/RoutePacer.App/Components src/RoutePacer.App/Formatting src/RoutePacer.App/wwwroot/css/tracker.css tests/RoutePacer.Core.Tests/Tracking tests/RoutePacer.App.Tests
git commit -m "fix: address rider workflow review"
git push -u origin HEAD
```

- [ ] Continue to Task 18 only after the reviewer explicitly approves this gate.

### Task 18: Harden the PWA Shell and Prove Offline Operation

**Files:**
- Modify: `src/RoutePacer.App/wwwroot/manifest.webmanifest`
- Modify: `src/RoutePacer.App/wwwroot/service-worker.js`
- Modify: `src/RoutePacer.App/wwwroot/service-worker.published.js`
- Modify: `src/RoutePacer.App/wwwroot/index.html`
- Create: `src/RoutePacer.App/wwwroot/icons/icon-192.png`
- Create: `src/RoutePacer.App/wwwroot/icons/icon-512.png`
- Create: `tests/RoutePacer.E2E/OfflinePwaTests.cs`
- Create: `tests/RoutePacer.E2E/TrackingCapabilityTests.cs`
- Create: `tests/RoutePacer.E2E/RouteTimerInvocationBrowserTests.cs`
- Create: `tests/RoutePacer.E2E/Fixtures/timed-route.gpx`
- Create: `docs/manual-validation.md`

**Interfaces:**
- Consumes: published PWA, browser context permissions, fixture routes.
- Produces: installable manifest, versioned app-shell caching, stale-while-revalidate static assets, and automated browser acceptance coverage.

- [ ] **Step 1: Write failing Playwright acceptance tests**

Create tests that publish and serve `RoutePacer.Server`, load it once online, import a route, set the browser context offline, reload `/routes`, verify the route remains, start a mocked-geolocation ride, push two positions, stop, reload `/rides`, and verify persistence. For invocation, use the same-origin relay test host and fixed Contract v1 fixture; prove signature verification, exact GPX import, one GET, immediate second-fetch `404`, URL cleanup, and ready-to-start navigation.

- [ ] **Step 2: Run the E2E tests to verify failure**

Run:

```bash
dotnet build src/RoutePacer.Server/RoutePacer.Server.csproj -c Release
dotnet test tests/RoutePacer.E2E --filter "FullyQualifiedName~OfflinePwaTests|FullyQualifiedName~TrackingCapabilityTests|FullyQualifiedName~RouteTimerInvocationBrowserTests"
```

Expected: FAIL until cache policy, icons, and browser harness are complete.

- [ ] **Step 3: Configure the installable manifest**

Set `name` to `RoutePacer`, `short_name` to `RoutePacer`, `start_url` to `/`, `scope` to `/`, `display` to `standalone`, portrait orientation, theme/background colors with tracker contrast, and 192/512 maskable icons. Keep `/open` as an ordinary navigation route; do not register a share target in MVP.

- [ ] **Step 4: Implement safe cache upgrades**

Use a versioned cache prefix, precache the generated Blazor asset manifest, delete only old RoutePacer caches on activate, serve navigation from cached `index.html` on network failure, and use stale-while-revalidate for same-origin static GET assets. Bypass `/api`, `/health`, every `/open` navigation containing a query, and every non-GET request; never call `cache.put` for those requests or their responses.

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
dotnet publish src/RoutePacer.Server/RoutePacer.Server.csproj -c Release
dotnet test RoutePacer.slnx -c Release --no-restore
```

Expected: all unit, component, and browser tests pass; the published app contains manifest, icons, service worker, WASM assets, and no signing secret.

- [ ] **Step 8: Commit**

```bash
git add src/RoutePacer.App/wwwroot tests/RoutePacer.E2E docs/manual-validation.md
git commit -m "test: verify installable offline RoutePacer PWA"
git push -u origin HEAD
```

### Task 19: Add Container, Health, Caddy, and Forward-Only Deployment

**Files:**
- Modify: `.gitignore`
- Create: `Dockerfile`
- Create: `.dockerignore`
- Create: `.github/workflows/publish-container.yml`
- Create: `deploy/docker-compose.yml`
- Create: `deploy/docker-compose.local.yml`
- Create: `deploy/.env.example`
- Create: `deploy/caddy/routepacer.caddy`
- Create: `deploy/README.md`
- Create: `src/RoutePacer.Server/Health/MigrationState.cs`
- Create: `src/RoutePacer.Server/Health/DatabaseMigrationService.cs`
- Create: `src/RoutePacer.Server/Health/MigrationsReadyHealthCheck.cs`
- Modify: `src/RoutePacer.Server/Program.cs`
- Create: `tests/RoutePacer.Server.Tests/Health/HealthEndpointTests.cs`
- Create: `tests/RoutePacer.Server.Tests/Health/DatabaseMigrationServiceTests.cs`
- Create: `tests/RoutePacer.E2E/DeploymentConfigurationTests.cs`
- Create: `tests/RoutePacer.E2E/SensitiveLoggingTests.cs`
- Create: `tests/RoutePacer.E2E/ProductionLikeHandoffTests.cs`

**Interfaces:**
- Consumes: completed hosted application, PostgreSQL migration, relay and intake feature controls.
- Produces: multi-arch container image, dedicated internal PostgreSQL deployment, migration-gated `/health/ready`, shared-Caddy route, forward-only deployment runbook, production-like acceptance test, and ignored captured-log evidence under `artifacts/test-logs`.

- [ ] **Step 1: Write failing health and deployment-shape tests**

Health tests require anonymous `/health/live` to return `200` when the process runs and `/health/ready` to return `503` until PostgreSQL is reachable and migrations complete, then `200`. Deployment tests parse `docker compose config --format json` and assert:

```csharp
Assert.False(Service("routepacer-db").TryGetProperty("ports", out _));
Assert.False(Service("routepacer").TryGetProperty("ports", out _));
Assert.Equal(true, Network("routepacer-private").GetProperty("internal").GetBoolean());
Assert.Contains("routepacer-private", ServiceNetworks("routepacer-db"));
Assert.DoesNotContain("mcp-public", ServiceNetworks("routepacer-db"));
Assert.Contains("mcp-public", ServiceNetworks("routepacer"));
```

Also assert the Compose file defines one named PostgreSQL volume, no backup service, disabled upload/intake defaults, a PostgreSQL health dependency, and an app readiness healthcheck.

- [ ] **Step 2: Run health and deployment tests to verify failure**

Run:

```bash
dotnet test tests/RoutePacer.Server.Tests --filter FullyQualifiedName~Health
dotnet test tests/RoutePacer.E2E --filter FullyQualifiedName~DeploymentConfigurationTests
```

Expected: FAIL because health services and deployment files are absent.

- [ ] **Step 3: Implement serialized startup migration and health endpoints**

`DatabaseMigrationService.StartAsync` opens PostgreSQL, acquires a fixed application advisory lock, applies `MigrateAsync`, sets `MigrationState.IsComplete = true`, and releases the lock. Failure leaves readiness unhealthy and stops startup. Register database and migration readiness checks tagged `ready`, then map:

```csharp
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false,
    ResponseWriter = static (context, _) => context.Response.WriteAsync("Healthy")
});
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
    ResponseWriter = static (context, _) => context.Response.WriteAsync("Healthy")
});
```

- [ ] **Step 4: Build the container image**

Use `mcr.microsoft.com/dotnet/sdk:10.0` to restore and publish `RoutePacer.Server`, then copy into `mcr.microsoft.com/dotnet/aspnet:10.0`. Run as a non-root user, expose 8080, set `ASPNETCORE_HTTP_PORTS=8080`, and add `HEALTHCHECK CMD curl -f http://127.0.0.1:8080/health/ready || exit 1`. `.dockerignore` excludes `.git`, build outputs, test results, deployment secrets, GPX/FIT files, and local environment files.

- [ ] **Step 5: Create production and local Compose definitions**

Production uses `postgres:16-alpine`, `restart: unless-stopped`, the named `routepacer_postgres` volume, `routepacer-private` with `internal: true`, and external `mcp-public`. `routepacer` pulls `ghcr.io/jamiemitchellconsultants/routepacer:${ROUTEPACER_IMAGE_TAG:-latest}`, waits for database health, applies migrations, and receives runtime values only through environment variables:

```yaml
ConnectionStrings__RoutePacer: Host=routepacer-db;Database=${ROUTEPACER_DB_NAME:-routepacer};Username=${ROUTEPACER_DB_USER:-routepacer};Password=${ROUTEPACER_DB_PASSWORD:?set ROUTEPACER_DB_PASSWORD}
Database__ApplyMigrations: "true"
HandoffRelay__UploadsEnabled: ${ROUTEPACER_RELAY_UPLOADS_ENABLED:-false}
HandoffRelay__PublicOrigin: https://pacetracking.tqaentry.com
HandoffRelay__UploadCredential: ${ROUTEPACER_RELAY_UPLOAD_KEY:-}
RouteTimerInvocation__Enabled: ${ROUTEPACER_ROUTE_TIMER_INTAKE_ENABLED:-false}
RouteTimerInvocation__PublicKeyJwk: ${ROUTEPACER_ROUTE_TIMER_PUBLIC_JWK:-}
```

Local Compose follows the same two-service topology but publishes the app only on `127.0.0.1:${ROUTEPACER_PORT:-49216}:8080`, uses documented local-only database credentials, and keeps both handoff features disabled.

- [ ] **Step 6: Add Caddy routing with sensitive access logs disabled**

Create:

```caddyfile
# Copy to the shared Caddy conf.d directory, validate, then reload Caddy.
pacetracking.tqaentry.com {
    log {
        output discard
    }
    reverse_proxy routepacer:8080
}
```

The complete Caddy configuration must validate before reload. Discard site access logs so `/api/handoffs/{token}` and `/open?...` can never reach ingress logs; aggregate relay metrics come from safe application counters.

- [ ] **Step 7: Write the forward-only deployment runbook**

`deploy/README.md` mirrors RouteTimer's numbered style: provision the database password, relay upload key, and public JWK outside source control; leave both RoutePacer controls and RouteTimer handoff disabled; set immutable `ROUTEPACER_IMAGE_TAG`; run `docker compose -f deploy/docker-compose.yml up -d --pull always --wait`; copy/validate/reload the Caddy fragment; curl public readiness; run fixtures and smoke tests; enable RoutePacer intake and uploads; then enable RouteTimer. State explicitly that the relay database has no backup, restore, or rollback procedure. Failures are corrected forward and redeployed; the disposable database may be recreated.

- [ ] **Step 8: Add captured-log and production-like tests**

Add `artifacts/test-logs/` to `.gitignore`. `SensitiveLoggingTests` resolves the repository root by walking upward from `AppContext.BaseDirectory` until it finds `RoutePacer.slnx`, creates `artifacts/test-logs`, deletes stale files there, sends a canary credential, token, payload URL, signed query, route name, and GPX marker through success and failure paths, and writes the complete captured application logs to `artifacts/test-logs/sensitive-logging.log`. Assert the file is non-empty and none of the canaries appear. The production-like test starts private-only RouteTimer, public-context RoutePacer plus PostgreSQL, uploads outbound, opens the signed URL in a phone-sized Playwright context, verifies exact IndexedDB import and ready state, queries PostgreSQL for zero rows, and asserts a direct second GET is `404`.

- [ ] **Step 9: Add container publishing workflow**

On pushes to `main` and version tags, restore, build, test, build `linux/amd64` and `linux/arm64`, and publish GHCR tags `latest`, commit SHA, and semantic version only after all gates pass. Grant only `contents: read` and `packages: write`.

- [ ] **Step 10: Verify deployment artifacts**

Run:

```bash
ROUTEPACER_DB_PASSWORD=test ROUTEPACER_RELAY_UPLOAD_KEY=test-only ROUTEPACER_ROUTE_TIMER_PUBLIC_JWK='{}' docker compose -f deploy/docker-compose.yml config --quiet
docker compose -f deploy/docker-compose.local.yml config --quiet
docker build -t routepacer:test .
dotnet test tests/RoutePacer.Server.Tests --filter FullyQualifiedName~Health
dotnet test tests/RoutePacer.E2E --filter "FullyQualifiedName~DeploymentConfigurationTests|FullyQualifiedName~SensitiveLoggingTests|FullyQualifiedName~ProductionLikeHandoffTests"
test -s artifacts/test-logs/sensitive-logging.log
```

Expected: Compose and image build exit `0`; tests PASS; database has no published port or backup service.

- [ ] **Step 11: Commit**

```bash
git add .gitignore Dockerfile .dockerignore .github/workflows/publish-container.yml deploy src/RoutePacer.Server/Health src/RoutePacer.Server/Program.cs tests/RoutePacer.Server.Tests/Health tests/RoutePacer.E2E
git commit -m "deploy: host RoutePacer relay behind Caddy"
git push -u origin HEAD
```

### Task 20: Add Performance Regression Coverage and Release Documentation

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

`docs/architecture.md` describes hosted project boundaries, relay request flow, atomic `DELETE ... RETURNING`, import normalization, IndexedDB schema/versioning, matching/pacing formulas, session transitions, and cache exclusions. `docs/privacy.md` states that manual imports, imported routes, rides, and tracking remain on-device; an explicit RouteTimer handoff temporarily processes readable route/location data in PostgreSQL for at most 10 minutes; successful consumption deletes immediately; expired rows are cleaned automatically; TLS protects transit; and no relay backup exists.

- [ ] **Step 4: Document the coordinated RouteTimer rollout**

Sequence deployment as: publish RoutePacer with relay uploads and intake disabled; provision the shared upload credential and RouteTimer P-256 key; configure RoutePacer with only the public JWK; configure private RouteTimer with the upload credential and private key while its handoff stays disabled; run shared fixtures and the production-like private-to-public flow; enable RoutePacer intake and uploads; then enable RouteTimer. Include exact readiness, first-fetch, immediate-row-deletion, second-fetch-`404`, expiry, manual-fallback, and real-phone QR smoke steps. State that there is no rollback, backup, or restore procedure.

- [ ] **Step 5: Run final repository checks**

Run:

```bash
test -d src/RoutePacer.App/wwwroot
! rg -n "PRIVATE KEY|HMACSHA|UploadCredential|RelayUpload" src/RoutePacer.App/wwwroot
test -s artifacts/test-logs/sensitive-logging.log
! rg -n "route name canary|gpx log canary|payload token canary|relay credential canary" artifacts/test-logs
dotnet format RoutePacer.slnx --verify-no-changes
dotnet build RoutePacer.slnx -c Release
dotnet test RoutePacer.slnx -c Release --no-build
git status --short
```

Expected: both sensitive scans return no matches; formatting, build, and tests pass; status contains only intentional documentation or generated Playwright artifacts that are either committed deliberately or ignored.

- [ ] **Step 6: Commit**

```bash
git add tests README.md docs/architecture.md docs/privacy.md docs/route-timer-rollout.md
git commit -m "docs: complete RoutePacer release guidance"
git push -u origin HEAD
```

### Review Gate 5: Release Acceptance

- [ ] Stop at a clean checkout of the pushed branch. Run the Task 19 production-like deployment tests first so they create `artifacts/test-logs/sensitive-logging.log`, then run the Task 18 release acceptance suite and the Task 20 full verification commands; every command must pass.
- [ ] Review Tasks 18–20 against every row in the MVP acceptance traceability table and every acceptance criterion in `docs/superpowers/specs/2026-08-27-routepacer-public-handoff-relay-design.md`.
- [ ] Complete the iOS, Android, desktop, large-route, long-ride, airplane-mode, real-QR, second-fetch, expiry-cleanup, private-RouteTimer-networking, and manual-fallback entries in `docs/manual-validation.md` with evidence and explicit pass/fail results.
- [ ] Confirm the container image is immutable, both feature controls remain disabled by default, PostgreSQL has no public port or backup path, Caddy and application logging are redacted, and the documented enablement order is exact.
- [ ] Record every finding, implement corrections, and rerun the complete release verification.
- [ ] Stage only release correction files, commit, and push:

```bash
git add .gitignore src tests Dockerfile .dockerignore .github/workflows/publish-container.yml deploy README.md docs/architecture.md docs/privacy.md docs/route-timer-rollout.md docs/manual-validation.md
git commit -m "fix: address release acceptance review"
git push -u origin HEAD
```

- [ ] Mark implementation complete only after the final reviewer explicitly approves this gate.

---

## MVP Acceptance Traceability

| Source requirement | Implemented and proven by |
|---|---|
| Installable hosted PWA and offline startup | Tasks 1, 18, and 19 |
| GPX and FIT import | Tasks 3–5 and 7 |
| IndexedDB route/ride persistence | Tasks 6, 7, 15, 17, and 18 |
| Select route and start tracking | Tasks 7, 12, 13, and 16 |
| High-accuracy active-screen GPS | Tasks 14–16 and 18 |
| Route projection and position | Task 12 |
| Distance and time lead/lag | Tasks 13 and 16 |
| Best-effort Wake Lock and recovery | Tasks 14, 16, and 18 |
| Ride history available offline | Tasks 15, 17, and 18 |
| Authenticated short-lived relay | Tasks 8–10 and 19 |
| RouteTimer auto-import-to-ready flow | Tasks 11, 18, and 19 |
| Distance-only fallback for untimed routes | Tasks 3, 13, 16, and 18 |
| Plaintext relay privacy and on-device retention after import | Tasks 8–11, 17, 19, and 20 |
| Large-route and long-ride stability | Tasks 12, 15, 18, and 20 |
| Docker/PostgreSQL/Caddy deployment | Task 19 |

## Deferred Enhancement

Web Share Target support remains outside MVP. After Contract v1 is deployed and stable, add a separate design and plan covering manifest `share_target`, service-worker multipart intake, temporary Cache Storage transfer, MIME/size validation, duplicate-import prevention, and iOS/Android support testing.
