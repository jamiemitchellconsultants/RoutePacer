using RoutePacer.App.Browser;
using RoutePacer.Core.Domain;
using RoutePacer.Core.Storage;
using RoutePacer.Core.Tracking;

namespace RoutePacer.App.Rides;

public sealed class RideSessionService : IAsyncDisposable
{
    /// <summary>Snapshots reach the UI at most this often. Every accepted fix is still persisted.</summary>
    public static readonly TimeSpan PublishInterval = TimeSpan.FromMilliseconds(250);

    private readonly IRouteRepository routes; private readonly IRideRepository rides;
    private readonly ILocationService location; private readonly IWakeLockService wakeLock; private readonly TimeProvider clock;
    private readonly RouteMatcher matcher; private readonly PacingService pacer;
    private readonly GpsSpikeFilter filter = new();

    private RouteTrack? route; private RideSummary? ride;
    private DateTimeOffset started; private DateTimeOffset? pausedAt; private TimeSpan pausedTotal;
    private GeoFix? previousFix; private double totalDistance; private int? previousSegment; private long sequence;
    private double lastRouteDistance;
    private string? statusMessage; private double? lastAccuracy; private WakeLockStatus wakeStatus = WakeLockStatus.Unsupported;
    private DateTimeOffset lastPublished = DateTimeOffset.MinValue;

    public RideSessionService(IRouteRepository routes, IRideRepository rides, ILocationService location, IWakeLockService wakeLock, TimeProvider clock, RouteMatcher? matcher = null, PacingService? pacer = null)
    {
        this.routes = routes; this.rides = rides; this.location = location; this.wakeLock = wakeLock; this.clock = clock;
        this.matcher = matcher ?? new RouteMatcher(); this.pacer = pacer ?? new PacingService();
        wakeLock.StatusChanged += OnWakeStatusChanged;
    }

    public RideSessionState State { get; private set; } = RideSessionState.Idle;
    public TrackerSnapshot? Snapshot { get; private set; }

    /// <summary>A finished or faulted session is not active, so the rider can start another ride without reloading.</summary>
    public bool Active => State is RideSessionState.Starting or RideSessionState.Running or RideSessionState.Paused or RideSessionState.Stopping;

    public event Action<TrackerSnapshot>? SnapshotChanged;

    public async Task StartAsync()
    {
        if (Active) throw new InvalidOperationException("A ride is already active.");
        Snapshot = null;
        route = await routes.GetAsync() ?? throw new InvalidOperationException("No route is loaded.");
        var routeId = route.Summary.RouteId;
        State = RideSessionState.Starting;
        started = clock.GetUtcNow(); pausedTotal = TimeSpan.Zero; pausedAt = null;
        previousFix = null; totalDistance = 0; sequence = 0; previousSegment = null; lastRouteDistance = 0;
        statusMessage = null; lastAccuracy = null; lastPublished = DateTimeOffset.MinValue;

        ride = new RideSummary(Guid.NewGuid(), routeId, started, null, RideStatus.Running, 0, 0, 0);
        await rides.StartAsync(ride);
        // A wake lock is best effort; losing it must never prevent a ride from starting.
        try { await wakeLock.AcquireAsync(); }
        catch { wakeStatus = WakeLockStatus.Failed; }
        try { await location.StartAsync(OnFixAsync, OnLocationErrorAsync); State = RideSessionState.Running; Publish(null, force: true); }
        catch { await StopBrowserServicesAsync(); await FinalizeAsync(RideStatus.Interrupted); throw; }
    }

    public async Task PauseAsync()
    {
        if (State != RideSessionState.Running) throw new InvalidOperationException("Ride is not running.");
        await StopBrowserServicesAsync();
        pausedAt = clock.GetUtcNow(); State = RideSessionState.Paused;
        if (ride is not null)
        {
            ride = ride with { Status = RideStatus.Paused, DurationSeconds = CurrentDuration().TotalSeconds, TotalDistanceMeters = totalDistance };
            await rides.SaveAsync(ride);
        }
        Publish(Snapshot?.Pacing, force: true);
    }

    public async Task ResumeAsync()
    {
        if (State != RideSessionState.Paused) throw new InvalidOperationException("Ride is not paused.");
        if (pausedAt is { } pause) pausedTotal += clock.GetUtcNow() - pause;
        pausedAt = null;
        try { await wakeLock.AcquireAsync(); } catch { wakeStatus = WakeLockStatus.Failed; }
        await location.StartAsync(OnFixAsync, OnLocationErrorAsync);
        State = RideSessionState.Running;
        if (ride is not null) { ride = ride with { Status = RideStatus.Running }; await rides.SaveAsync(ride); }
        Publish(Snapshot?.Pacing, force: true);
    }

    public async Task StopAsync()
    {
        if (State is not (RideSessionState.Running or RideSessionState.Paused)) throw new InvalidOperationException("Ride is not active.");
        State = RideSessionState.Stopping;
        await StopBrowserServicesAsync();
        await FinalizeAsync(RideStatus.Completed);
    }

    /// <summary>
    /// Restores a ride left in progress by a crash, a reload, or an evicted tab. It comes back
    /// <see cref="RideSessionState.Paused"/>, never Running: resuming starts GPS, and location
    /// permission is never requested before the rider asks for it.
    /// </summary>
    public async Task RestoreActiveRideAsync()
    {
        if (Active) return;
        var active = await rides.GetActiveAsync();
        if (active is null) return;
        if (active.Summary.Status is not (RideStatus.Running or RideStatus.Paused)) { await rides.ClearAsync(); return; }

        route = await routes.GetAsync();
        if (route is null || route.Summary.RouteId != active.Summary.RouteId)
        {
            // The route was replaced while the ride was away. Its recorded points are measured
            // against a route that is no longer here, so the ride cannot be resumed meaningfully.
            await rides.ClearAsync();
            return;
        }

        ride = active.Summary with { Status = RideStatus.Paused };
        started = active.Summary.StartedAtUtc;
        totalDistance = active.Summary.TotalDistanceMeters;
        sequence = active.Points.Count == 0 ? 0 : active.Points[^1].Sequence + 1;
        previousSegment = null;   // Rebuilt by the next match; a full scan once costs less than storing it.
        previousFix = null;
        // Progress along the route survives, so the recovered ride shows where it had reached
        // rather than snapping the rider back to the start line.
        lastRouteDistance = active.Points.Count == 0 ? 0 : active.Points[^1].ProjectedRouteDistanceMeters ?? 0;
        filter.Reset();

        // Elapsed resumes from the last duration actually observed. The gap while the app was gone
        // was not measured, and counting it would silently inflate every delta the rider reads.
        var now = clock.GetUtcNow();
        pausedTotal = now - started - TimeSpan.FromSeconds(active.Summary.DurationSeconds);
        if (pausedTotal < TimeSpan.Zero) pausedTotal = TimeSpan.Zero;
        pausedAt = now;

        statusMessage = "Ride recovered and paused. Resume when you are ready.";
        State = RideSessionState.Paused;
        await rides.SaveAsync(ride);
        Publish(null, force: true);
    }

    private async Task OnFixAsync(GeoFix fix)
    {
        if (State != RideSessionState.Running || route is null || ride is null || !filter.Accept(fix)) return;
        lastAccuracy = fix.AccuracyMeters;
        var match = matcher.Match(route, fix, previousSegment);
        if (match is null) { statusMessage = "Off route — waiting to rejoin."; Publish(Snapshot?.Pacing); return; }
        statusMessage = null;
        previousSegment = match.SegmentIndex; lastRouteDistance = match.RouteDistanceMeters;
        if (previousFix is not null) totalDistance += GeoMath.HaversineMeters(previousFix.Latitude, previousFix.Longitude, fix.Latitude, fix.Longitude);
        previousFix = fix;
        var pacing = pacer.Calculate(route, match, started, PausedSoFar(), fix);
        var point = new RidePoint(ride.RideId, sequence++, fix.TimestampUtc, fix.Latitude, fix.Longitude, fix.SpeedMps, fix.AccuracyMeters, match.RouteDistanceMeters, pacing.DeltaDistanceMeters, pacing.DeltaTimeSeconds, match.CrossTrackErrorMeters);
        await rides.AppendPointAsync(point);
        Publish(pacing);
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
            LocationFailure.Timeout => "Waiting for a GPS fix…",
            LocationFailure.Unavailable => "GPS signal is unavailable right now.",
            _ => "GPS reported a problem; still trying."
        };
        Publish(Snapshot?.Pacing, force: true);
    }

    private void OnWakeStatusChanged(WakeLockStatus status) { wakeStatus = status; Publish(Snapshot?.Pacing, force: true); }

    private async Task StopBrowserServicesAsync() { await location.StopAsync(); await wakeLock.ReleaseAsync(); }

    private TimeSpan PausedSoFar() => pausedTotal + (pausedAt is { } at ? clock.GetUtcNow() - at : TimeSpan.Zero);

    private TimeSpan CurrentDuration()
        => TimeSpan.FromSeconds(Math.Max(0, (clock.GetUtcNow() - started - PausedSoFar()).TotalSeconds));

    private async Task FinalizeAsync(RideStatus status)
    {
        if (ride is null) return;
        var duration = CurrentDuration();
        ride = ride with
        {
            Status = status,
            EndedAtUtc = clock.GetUtcNow(),
            TotalDistanceMeters = totalDistance,
            DurationSeconds = duration.TotalSeconds,
            AvgSpeedMps = duration.TotalSeconds > 0 ? totalDistance / duration.TotalSeconds : 0
        };
        // The finished ride is cleared, not stored. It stays in Snapshot so the rider can read the
        // final numbers on this page, and goes when they leave it.
        await rides.ClearAsync();
        State = status == RideStatus.Completed ? RideSessionState.Completed : RideSessionState.Faulted;
        Publish(Snapshot?.Pacing, force: true);
    }

    private void Publish(PacingSnapshot? pacing, bool force = false)
    {
        if (route is null) return;
        Snapshot = new TrackerSnapshot(State, route.Summary, pacing, pacing?.Match.RouteDistanceMeters ?? lastRouteDistance,
            CurrentDuration(), route.HasTiming, sequence, lastAccuracy, wakeStatus, statusMessage);
        var now = clock.GetUtcNow();
        if (!force && now - lastPublished < PublishInterval) return;
        lastPublished = now;
        SnapshotChanged?.Invoke(Snapshot);
    }

    public ValueTask DisposeAsync() { wakeLock.StatusChanged -= OnWakeStatusChanged; return ValueTask.CompletedTask; }
}
