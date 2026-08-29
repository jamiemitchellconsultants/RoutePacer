using RoutePacer.App.Browser;
using RoutePacer.App.Storage;
using RoutePacer.Core.Domain;
using RoutePacer.Core.Storage;
using RoutePacer.Core.Tracking;

namespace RoutePacer.App.Tests;

public sealed record ModuleCall(string Name, object?[] Args);

/// <summary>Records the JS module calls a repository makes without touching a browser.</summary>
public sealed class RecordingIndexedDbModule : IIndexedDbModule
{
    public List<ModuleCall> Calls { get; } = [];
    public Dictionary<string, object?> Results { get; } = [];

    public ValueTask<T?> InvokeAsync<T>(string identifier, object?[]? args = null)
    {
        Calls.Add(new(identifier, args ?? []));
        return ValueTask.FromResult(Results.TryGetValue(identifier, out var value) ? (T?)value : default);
    }

    public ValueTask InvokeVoidAsync(string identifier, object?[]? args = null)
    {
        Calls.Add(new(identifier, args ?? []));
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

public sealed class InMemoryRouteRepository : IRouteRepository
{
    private RouteTrack? track;
    public int ClearCount { get; private set; }

    public Task SaveAsync(RouteTrack route, CancellationToken cancellationToken = default)
    {
        // Replaces, exactly as the IndexedDB implementation does in one transaction.
        track = route;
        return Task.CompletedTask;
    }

    public Task<RouteTrack?> GetAsync(CancellationToken cancellationToken = default) => Task.FromResult(track);

    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        ClearCount++; track = null;
        return Task.CompletedTask;
    }
}

public sealed class InMemoryRideRepository : IRideRepository
{
    private RideSummary? active;
    public List<RidePoint> Points { get; } = [];
    public int ClearCount { get; private set; }

    public Task StartAsync(RideSummary ride, CancellationToken cancellationToken = default)
    {
        active = ride; Points.Clear();
        return Task.CompletedTask;
    }

    public Task SaveAsync(RideSummary ride, CancellationToken cancellationToken = default) { active = ride; return Task.CompletedTask; }
    public Task AppendPointAsync(RidePoint point, CancellationToken cancellationToken = default) { Points.Add(point); return Task.CompletedTask; }

    public Task<ActiveRide?> GetActiveAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(active is null ? null : new ActiveRide(active, Points.OrderBy(p => p.Sequence).ToArray()));

    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        ClearCount++; active = null; Points.Clear();
        return Task.CompletedTask;
    }

    /// <summary>Seeds an in-progress ride as a crash would have left it, for recovery tests.</summary>
    public void SeedActive(RideSummary ride, params RidePoint[] points)
    {
        active = ride; Points.Clear(); Points.AddRange(points);
    }
}

public sealed class FakeLocationService : ILocationService
{
    public int StartCount { get; private set; }
    public int StopCount { get; private set; }
    public bool Watching { get; private set; }
    public Exception? StartFailure { get; set; }

    private Func<GeoFix, Task>? onFix;
    private Func<LocationFailure, Task>? onError;

    public Task StartAsync(Func<GeoFix, Task> fix, Func<LocationFailure, Task> error, CancellationToken cancellationToken = default)
    {
        if (StartFailure is not null) throw StartFailure;
        onFix = fix; onError = error; StartCount++; Watching = true;
        return Task.CompletedTask;
    }

    public Task StopAsync() { StopCount++; Watching = false; return Task.CompletedTask; }
    public Task PushAsync(GeoFix fix) => onFix?.Invoke(fix) ?? Task.CompletedTask;
    public Task FailAsync(LocationFailure failure) => onError?.Invoke(failure) ?? Task.CompletedTask;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

public sealed class FakeWakeLockService : IWakeLockService
{
    public event Action<WakeLockStatus>? StatusChanged;
    public int AcquireCount { get; private set; }
    public int ReleaseCount { get; private set; }
    public Exception? AcquireFailure { get; set; }

    public Task AcquireAsync()
    {
        AcquireCount++;
        if (AcquireFailure is not null) throw AcquireFailure;
        StatusChanged?.Invoke(WakeLockStatus.Acquired);
        return Task.CompletedTask;
    }

    public Task ReleaseAsync() { ReleaseCount++; StatusChanged?.Invoke(WakeLockStatus.Released); return Task.CompletedTask; }
    public void Revoke() => StatusChanged?.Invoke(WakeLockStatus.Revoked);
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

public static class TrackFixtures
{
    public static RouteTrack Straight(Guid? routeId = null, int points = 21, double metresPerSecond = 10, bool timed = true, string name = "Test route")
    {
        var id = routeId ?? Guid.NewGuid();
        var step = 0.001;
        var list = new List<RoutePoint>(points);
        var distance = 0d;
        for (var i = 0; i < points; i++)
        {
            var longitude = i * step;
            if (i > 0) distance += GeoMath.HaversineMeters(0, (i - 1) * step, 0, longitude);
            list.Add(new RoutePoint(id, i, 0, longitude, null, distance, timed ? distance / metresPerSecond : null, null));
        }
        return new RouteTrack(
            new RouteSummary(id, name, RouteSourceType.Gpx, DateTimeOffset.UnixEpoch, distance,
                timed ? distance / metresPerSecond : null, points, 0, 0, 0, (points - 1) * step),
            list);
    }
}
