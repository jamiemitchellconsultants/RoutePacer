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
    private readonly Dictionary<Guid, RouteTrack> tracks = [];
    public int DeleteCount { get; private set; }

    public Task SaveAsync(RouteTrack route, CancellationToken cancellationToken = default)
    {
        tracks[route.Summary.RouteId] = route;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<RouteSummary>> ListAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<RouteSummary>>(tracks.Values.Select(t => t.Summary).OrderByDescending(s => s.ImportedAtUtc).ToArray());

    public Task<RouteTrack?> GetAsync(Guid routeId, CancellationToken cancellationToken = default)
        => Task.FromResult(tracks.GetValueOrDefault(routeId));

    public Task DeleteAsync(Guid routeId, CancellationToken cancellationToken = default)
    {
        DeleteCount++; tracks.Remove(routeId);
        return Task.CompletedTask;
    }
}

public sealed class InMemoryRideRepository : IRideRepository
{
    private readonly Dictionary<Guid, RideSummary> summaries = [];
    public List<RidePoint> Points { get; } = [];
    public int DeleteCount { get; private set; }

    public Task CreateAsync(RideSummary ride, CancellationToken cancellationToken = default) { summaries[ride.RideId] = ride; return Task.CompletedTask; }
    public Task AppendPointAsync(RidePoint point, CancellationToken cancellationToken = default) { Points.Add(point); return Task.CompletedTask; }
    public Task CompleteAsync(RideSummary ride, CancellationToken cancellationToken = default) { summaries[ride.RideId] = ride; return Task.CompletedTask; }
    public Task<IReadOnlyList<RideSummary>> ListAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<RideSummary>>(summaries.Values.OrderByDescending(s => s.StartedAtUtc).ToArray());
    public Task<IReadOnlyList<RidePoint>> GetPointsAsync(Guid rideId, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<RidePoint>>(Points.Where(p => p.RideId == rideId).OrderBy(p => p.Sequence).ToArray());
    public Task DeleteAsync(Guid rideId, CancellationToken cancellationToken = default)
    {
        DeleteCount++; summaries.Remove(rideId); Points.RemoveAll(p => p.RideId == rideId);
        return Task.CompletedTask;
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
