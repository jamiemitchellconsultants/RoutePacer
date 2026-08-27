# RoutePacer
cycling training app that is a stand alone Blazor wasm app that can download a gpx or Garmin fit file with locations and times in it. I want the user to start a ride and for the app to track how far ahead or behind the rider is compared to the gpx/fit file in time and distance. The tracking / pacing part of the app will work off-line and use gps

## Planning document

- `OFFLINE_FIRST_BLAZOR_CYCLING_APP_PLAN.md`: detailed offline-first implementation plan, including RouteTimer-to-RoutePacer GPX invocation flow.

## Local development

```bash
dotnet restore RoutePacer.slnx
dotnet build RoutePacer.slnx --no-restore
dotnet test RoutePacer.slnx --no-build
dotnet run --project src/RoutePacer.Server/RoutePacer.Server.csproj
```
