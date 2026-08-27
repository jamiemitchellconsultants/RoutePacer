using RoutePacer.Core.Domain;

namespace RoutePacer.Core.Storage;

public interface IRideRepository
{
    Task CreateAsync(RideSummary ride, CancellationToken cancellationToken = default);
    Task AppendPointAsync(RidePoint point, CancellationToken cancellationToken = default);
    Task CompleteAsync(RideSummary ride, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RideSummary>> ListAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RidePoint>> GetPointsAsync(Guid rideId, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid rideId, CancellationToken cancellationToken = default);
}
