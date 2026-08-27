using RoutePacer.Core.Domain;

namespace RoutePacer.Core.Storage;

public interface IRouteRepository
{
    Task SaveAsync(RouteTrack route, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RouteSummary>> ListAsync(CancellationToken cancellationToken = default);
    Task<RouteTrack?> GetAsync(Guid routeId, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid routeId, CancellationToken cancellationToken = default);
}
