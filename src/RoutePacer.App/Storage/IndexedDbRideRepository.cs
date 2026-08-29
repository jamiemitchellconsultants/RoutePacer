using RoutePacer.Core.Domain;
using RoutePacer.Core.Storage;

namespace RoutePacer.App.Storage;

public sealed class IndexedDbRideRepository(IIndexedDbModule db) : IRideRepository
{
    public Task StartAsync(RideSummary ride, CancellationToken cancellationToken = default) => db.InvokeVoidAsync("startRide", [ride]).AsTask();
    public Task SaveAsync(RideSummary ride, CancellationToken cancellationToken = default) => db.InvokeVoidAsync("saveActiveRide", [ride]).AsTask();
    public Task AppendPointAsync(RidePoint point, CancellationToken cancellationToken = default) => db.InvokeVoidAsync("appendRidePoint", [point]).AsTask();

    public async Task<ActiveRide?> GetActiveAsync(CancellationToken cancellationToken = default)
    {
        var dto = await db.InvokeAsync<ActiveRideDto>("getActiveRide").ConfigureAwait(false);
        return dto is null ? null : new ActiveRide(dto.Summary, dto.Points);
    }

    public Task ClearAsync(CancellationToken cancellationToken = default) => db.InvokeVoidAsync("clearRide").AsTask();

    public sealed record ActiveRideDto(RideSummary Summary, RidePoint[] Points);
}
