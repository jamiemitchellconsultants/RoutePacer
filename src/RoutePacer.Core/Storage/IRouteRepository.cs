using RoutePacer.Core.Domain;

namespace RoutePacer.Core.Storage;

/// <summary>
/// Holds at most one route. There is no identifier on any operation because there is nothing to
/// choose between: importing replaces whatever was there.
/// </summary>
public interface IRouteRepository
{
    Task SaveAsync(RouteTrack route, CancellationToken cancellationToken = default);
    Task<RouteTrack?> GetAsync(CancellationToken cancellationToken = default);
    Task ClearAsync(CancellationToken cancellationToken = default);
}
