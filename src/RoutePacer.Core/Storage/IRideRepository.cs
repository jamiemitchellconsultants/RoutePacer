using RoutePacer.Core.Domain;

namespace RoutePacer.Core.Storage;

/// <summary>
/// Holds at most one in-progress ride, so a reload or an evicted tab does not end a ride mid-route.
/// A finished ride is cleared, never kept: the rider's own head unit or phone app is what records
/// the ride, and this application is a pacing aide.
/// </summary>
public interface IRideRepository
{
    Task StartAsync(RideSummary ride, CancellationToken cancellationToken = default);
    Task SaveAsync(RideSummary ride, CancellationToken cancellationToken = default);
    Task AppendPointAsync(RidePoint point, CancellationToken cancellationToken = default);
    Task<ActiveRide?> GetActiveAsync(CancellationToken cancellationToken = default);
    Task ClearAsync(CancellationToken cancellationToken = default);
}
