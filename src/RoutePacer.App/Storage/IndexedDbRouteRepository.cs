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
    public async Task<IReadOnlyList<RouteSummary>> ListAsync(CancellationToken cancellationToken = default) => await db.InvokeAsync<RouteSummary[]>("listRoutes").ConfigureAwait(false) ?? [];
    public async Task<RouteTrack?> GetAsync(Guid routeId, CancellationToken cancellationToken = default)
    {
        var dto = await db.InvokeAsync<RouteDto>("getRoute", [routeId.ToString("D")]).ConfigureAwait(false);
        return dto is null ? null : new RouteTrack(dto.Summary, dto.Points);
    }
    public Task DeleteAsync(Guid routeId, CancellationToken cancellationToken = default) => db.InvokeVoidAsync("deleteRoute", [routeId.ToString("D")]).AsTask();

    public sealed record RouteDto(RouteSummary Summary, RoutePoint[] Points);
}
