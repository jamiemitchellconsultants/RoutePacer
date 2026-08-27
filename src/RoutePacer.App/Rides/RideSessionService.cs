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

    public async Task StartAsync(Guid routeId)
    {
        if (Active) throw new InvalidOperationException("A ride is already active.");
        Snapshot = null;
        route = await routes.GetAsync(routeId) ?? throw new InvalidOperationException("Route not found.");
        State = RideSessionState.Starting;
        started = clock.GetUtcNow(); pausedTotal = TimeSpan.Zero; pausedAt = null;
        previousFix = null; totalDistance = 0; sequence = 0; previousSegment = null;
        statusMessage = null; lastAccuracy = null; lastPublished = DateTimeOffset.MinValue;

        ride = new RideSummary(Guid.NewGuid(), routeId, started, null, RideStatus.Running, 0, 0, 0);
        await rides.CreateAsync(ride);
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
            await rides.CompleteAsync(ride);
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
        if (ride is not null) { ride = ride with { Status = RideStatus.Running }; await rides.CompleteAsync(ride); }
        Publish(Snapshot?.Pacing, force: true);
    }

    public async Task StopAsync()
    {
        if (State is not (RideSessionState.Running or RideSessionState.Paused)) throw new InvalidOperationException("Ride is not active.");
        State = RideSessionState.Stopping;
        await StopBrowserServicesAsync();
        await FinalizeAsync(RideStatus.Completed);
    }

    /// <summary>Marks rides left Running or Paused by a crash or reload as Interrupted. Never resumes GPS.</summary>
    public async Task RecoverInterruptedAsync()
    {
        foreach (var item in await rides.ListAsync())
            if (item.Status is RideStatus.Running or RideStatus.Paused)
                await rides.CompleteAsync(item with { Status = RideStatus.Interrupted, EndedAtUtc = clock.GetUtcNow() });
    }

    private async Task OnFixAsync(GeoFix fix)
    {
        if (State != RideSessionState.Running || route is null || ride is null || !filter.Accept(fix)) return;
        lastAccuracy = fix.AccuracyMeters;
        var match = matcher.Match(route, fix, previousSegment);
        if (match is null) { statusMessage = "Off route — waiting to rejoin."; Publish(Snapshot?.Pacing); return; }
        statusMessage = null;
        previousSegment = match.SegmentIndex;
        if (previousFix is not null) totalDistance += GeoMath.HaversineMeters(previousFix.Latitude, previousFix.Longitude, fix.Latitude, fix.Longitude);
        previousFix = fix;
        var pacing = pacer.Calculate(route, match, started, fix);
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

    private TimeSpan CurrentDuration()
    {
        var paused = pausedTotal + (pausedAt is { } at ? clock.GetUtcNow() - at : TimeSpan.Zero);
        return TimeSpan.FromSeconds(Math.Max(0, (clock.GetUtcNow() - started - paused).TotalSeconds));
    }

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
        await rides.CompleteAsync(ride);
        State = status == RideStatus.Completed ? RideSessionState.Completed : RideSessionState.Faulted;
        Publish(Snapshot?.Pacing, force: true);
    }

    private void Publish(PacingSnapshot? pacing, bool force = false)
    {
        if (route is null) return;
        Snapshot = new TrackerSnapshot(State, route.Summary, pacing, pacing?.Match.RouteDistanceMeters ?? Snapshot?.DistanceMeters ?? 0,
            CurrentDuration(), route.HasTiming, sequence, lastAccuracy, wakeStatus, statusMessage);
        var now = clock.GetUtcNow();
        if (!force && now - lastPublished < PublishInterval) return;
        lastPublished = now;
        SnapshotChanged?.Invoke(Snapshot);
    }

    public ValueTask DisposeAsync() { wakeLock.StatusChanged -= OnWakeStatusChanged; return ValueTask.CompletedTask; }
}
