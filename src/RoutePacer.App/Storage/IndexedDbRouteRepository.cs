using RoutePacer.Core.Domain;
using RoutePacer.Core.Storage;

namespace RoutePacer.App.Storage;

public sealed class IndexedDbRouteRepository(IIndexedDbModule db) : IRouteRepository
{
    public async Task SaveAsync(RouteTrack route, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await db.InvokeVoidAsync("saveRoute", [route.Summary, route.Points]).ConfigureAwait(false);
    }

    public async Task<RouteTrack?> GetAsync(CancellationToken cancellationToken = default)
    {
        var dto = await db.InvokeAsync<RouteDto>("getRoute").ConfigureAwait(false);
        return dto is null ? null : new RouteTrack(dto.Summary, dto.Points);
    }

    public Task ClearAsync(CancellationToken cancellationToken = default) => db.InvokeVoidAsync("clearRoute").AsTask();

    public sealed record RouteDto(RouteSummary Summary, RoutePoint[] Points);
}
