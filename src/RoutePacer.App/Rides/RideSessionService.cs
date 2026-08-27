using RoutePacer.App.Browser;
using RoutePacer.Core.Domain;
using RoutePacer.Core.Storage;
using RoutePacer.Core.Tracking;

namespace RoutePacer.App.Rides;

public sealed class RideSessionService(IRouteRepository routes, IRideRepository rides, ILocationService location, IWakeLockService wakeLock, TimeProvider clock)
{
    private readonly GpsSpikeFilter filter = new(); private RouteTrack? route; private RideSummary? ride; private DateTimeOffset started; private DateTimeOffset? pausedAt; private TimeSpan pausedTotal; private GeoFix? previousFix; private double totalDistance; private int? previousSegment; private long sequence; private string? statusMessage;
    public RideSessionState State { get; private set; } = RideSessionState.Idle; public TrackerSnapshot? Snapshot { get; private set; }
    public event Action<TrackerSnapshot>? SnapshotChanged;
    public async Task StartAsync(Guid routeId)
    {
        if (State is not RideSessionState.Idle) throw new InvalidOperationException("A ride is already active.");
        route = await routes.GetAsync(routeId) ?? throw new InvalidOperationException("Route not found."); State = RideSessionState.Starting; started = clock.GetUtcNow(); pausedTotal = TimeSpan.Zero; pausedAt = null; previousFix = null; totalDistance = 0; sequence = 0; previousSegment = null; statusMessage = null;
        ride = new RideSummary(Guid.NewGuid(), routeId, started, null, RideStatus.Running, 0, 0, 0); await rides.CreateAsync(ride); await wakeLock.AcquireAsync();
        try { await location.StartAsync(OnFixAsync, OnLocationErrorAsync); State = RideSessionState.Running; Publish(null); }
        catch { await FinalizeAsync(RideStatus.Interrupted); throw; }
    }
    public async Task PauseAsync() { if (State != RideSessionState.Running) throw new InvalidOperationException("Ride is not running."); await location.StopAsync(); await wakeLock.ReleaseAsync(); pausedAt = clock.GetUtcNow(); State = RideSessionState.Paused; if (ride is not null) { ride = ride with { Status = RideStatus.Paused, DurationSeconds = CurrentDuration().TotalSeconds, TotalDistanceMeters = totalDistance }; await rides.CompleteAsync(ride); } Publish(Snapshot?.Pacing); }
    public async Task ResumeAsync() { if (State != RideSessionState.Paused) throw new InvalidOperationException("Ride is not paused."); if (pausedAt is { } pause) pausedTotal += clock.GetUtcNow() - pause; pausedAt = null; await wakeLock.AcquireAsync(); await location.StartAsync(OnFixAsync, OnLocationErrorAsync); State = RideSessionState.Running; if (ride is not null) { ride = ride with { Status = RideStatus.Running }; await rides.CompleteAsync(ride); } Publish(Snapshot?.Pacing); }
    public async Task StopAsync() { if (State is not (RideSessionState.Running or RideSessionState.Paused)) throw new InvalidOperationException("Ride is not active."); State = RideSessionState.Stopping; await StopBrowserServicesAsync(); await FinalizeAsync(RideStatus.Completed); }
    public async Task RecoverInterruptedAsync() { foreach (var item in await rides.ListAsync()) if (item.Status is RideStatus.Running or RideStatus.Paused) await rides.CompleteAsync(item with { Status = RideStatus.Interrupted, EndedAtUtc = clock.GetUtcNow() }); }
    private async Task OnFixAsync(GeoFix fix)
    {
        if (State != RideSessionState.Running || route is null || ride is null || !filter.Accept(fix)) return;
        var match = new RouteMatcher().Match(route, fix, previousSegment); if (match is null) return; statusMessage = null; previousSegment = match.SegmentIndex; if (previousFix is not null) totalDistance += GeoMath.HaversineMeters(previousFix.Latitude, previousFix.Longitude, fix.Latitude, fix.Longitude); previousFix = fix; var pacing = new PacingService().Calculate(route, match, started, fix);
        var point = new RidePoint(ride.RideId, sequence++, fix.TimestampUtc, fix.Latitude, fix.Longitude, fix.SpeedMps, fix.AccuracyMeters, match.RouteDistanceMeters, pacing.DeltaDistanceMeters, pacing.DeltaTimeSeconds, match.CrossTrackErrorMeters); await rides.AppendPointAsync(point); Publish(pacing);
    }
    // Only a denied or absent geolocation capability is terminal. watchPosition reports a timeout for every
    // 5-second gap in coverage and keeps watching, so treating those as fatal would end most real rides early.
    private async Task OnLocationErrorAsync(LocationFailure failure)
    {
        if (failure is LocationFailure.PermissionDenied or LocationFailure.Unsupported)
        {
            statusMessage = failure is LocationFailure.PermissionDenied
                ? "Location permission was denied, so the ride was stopped."
                : "This browser cannot provide location, so the ride was stopped.";
            await StopBrowserServicesAsync();
            await FinalizeAsync(RideStatus.Interrupted);
            return;
        }
        statusMessage = failure switch
        {
            LocationFailure.Timeout => "Waiting for a GPS fix\u2026",
            LocationFailure.Unavailable => "GPS signal is unavailable right now.",
            _ => "GPS reported a problem; still trying."
        };
        Publish(Snapshot?.Pacing);
    }

    private async Task StopBrowserServicesAsync() { await location.StopAsync(); await wakeLock.ReleaseAsync(); }
    private TimeSpan CurrentDuration() { var paused = pausedTotal + (pausedAt is { } at ? clock.GetUtcNow() - at : TimeSpan.Zero); return TimeSpan.FromSeconds(Math.Max(0, (clock.GetUtcNow() - started - paused).TotalSeconds)); }
    private async Task FinalizeAsync(RideStatus status) { if (ride is null) return; var duration = CurrentDuration(); ride = ride with { Status = status, EndedAtUtc = clock.GetUtcNow(), TotalDistanceMeters = totalDistance, DurationSeconds = duration.TotalSeconds, AvgSpeedMps = duration.TotalSeconds > 0 ? totalDistance / duration.TotalSeconds : 0 }; await rides.CompleteAsync(ride); State = status == RideStatus.Completed ? RideSessionState.Completed : RideSessionState.Faulted; Publish(Snapshot?.Pacing); }
    private void Publish(PacingSnapshot? pacing) { if (route is not null) { Snapshot = new TrackerSnapshot(State, route.Summary, pacing, pacing?.Match.RouteDistanceMeters ?? 0, statusMessage); SnapshotChanged?.Invoke(Snapshot); } }
}
