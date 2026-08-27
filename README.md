# RoutePacer

An offline-first Blazor WebAssembly PWA for riding to a plan. Import a timed GPX or Garmin FIT route, start
a ride, and RoutePacer shows how far ahead or behind that route's schedule you are — in both time and
distance — using GPS, with no network connection required.

Routes, rides, and positions stay on the device. The only thing that ever leaves it is an explicit
RouteTimer handoff, which is off by default. See [docs/privacy.md](docs/privacy.md).

## Documentation

| Document | Contents |
|---|---|
| [docs/architecture.md](docs/architecture.md) | Project boundaries, import pipeline, matching and pacing formulas, ride states, IndexedDB schema, relay flow, cache policy |
| [docs/privacy.md](docs/privacy.md) | What stays on device, what briefly leaves it, what is never logged |
| [docs/contracts/route-timer-invocation-v1.md](docs/contracts/route-timer-invocation-v1.md) | The frozen RouteTimer Contract v1 and its shared fixture |
| [docs/route-timer-rollout.md](docs/route-timer-rollout.md) | Coordinated enablement order and smoke steps |
| [docs/manual-validation.md](docs/manual-validation.md) | Device matrix for what automation cannot reach |
| [deploy/README.md](deploy/README.md) | Forward-only deployment runbook |
| [OFFLINE_FIRST_BLAZOR_CYCLING_APP_PLAN.md](OFFLINE_FIRST_BLAZOR_CYCLING_APP_PLAN.md) | Original product brief |

## Local development

```bash
dotnet restore RoutePacer.slnx
dotnet build RoutePacer.slnx --no-restore
dotnet test RoutePacer.slnx --no-build
dotnet run --project src/RoutePacer.Server/RoutePacer.Server.csproj
```

## Tests

The suites have different prerequisites:

| Project | Needs |
|---|---|
| `RoutePacer.Core.Tests`, `RoutePacer.App.Tests` | nothing |
| `RoutePacer.Server.Tests` | nothing; the relay store and clock are faked |
| `RoutePacer.Persistence.Tests` | Docker, for a real PostgreSQL 16 container |
| `RoutePacer.E2E` | Docker, plus Playwright browsers |

Install the Playwright browsers once before running the browser suites:

```bash
pwsh tests/RoutePacer.E2E/bin/Release/net10.0/playwright.ps1 install chromium
```

The browser suites publish and run `RoutePacer.Server` themselves, so they exercise the real published PWA
rather than a test double.

## Running the full stack locally

```bash
docker compose -f deploy/docker-compose.local.yml up --build
```

This publishes the app on `127.0.0.1:49216` with both handoff controls disabled. Override the port with
`ROUTEPACER_PORT`.
