using RoutePacer.Core.Domain;
using RoutePacer.Core.Storage;

namespace RoutePacer.App.Storage;

public sealed class IndexedDbRideRepository(IIndexedDbModule db) : IRideRepository
{
    public Task CreateAsync(RideSummary ride, CancellationToken cancellationToken = default) => db.InvokeVoidAsync("createRide", [ride]).AsTask();
    public Task AppendPointAsync(RidePoint point, CancellationToken cancellationToken = default) => db.InvokeVoidAsync("appendRidePoint", [point]).AsTask();
    public Task CompleteAsync(RideSummary ride, CancellationToken cancellationToken = default) => db.InvokeVoidAsync("completeRide", [ride]).AsTask();
    public async Task<IReadOnlyList<RideSummary>> ListAsync(CancellationToken cancellationToken = default) => await db.InvokeAsync<RideSummary[]>("listRides").ConfigureAwait(false) ?? [];
    public async Task<IReadOnlyList<RidePoint>> GetPointsAsync(Guid rideId, CancellationToken cancellationToken = default) => await db.InvokeAsync<RidePoint[]>("getRidePoints", [rideId.ToString("D")]).ConfigureAwait(false) ?? [];
    public Task DeleteAsync(Guid rideId, CancellationToken cancellationToken = default) => db.InvokeVoidAsync("deleteRide", [rideId.ToString("D")]).AsTask();
}
