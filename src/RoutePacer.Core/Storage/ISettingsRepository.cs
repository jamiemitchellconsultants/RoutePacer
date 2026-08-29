using RoutePacer.Core.Domain;

namespace RoutePacer.Core.Storage;

/// <summary>
/// Rider preferences that outlive the route and the ride. Separate from
/// <see cref="IRouteRepository"/> precisely so that importing a route does not reset them.
/// </summary>
public interface ISettingsRepository
{
    Task<AutoPauseSettings> GetAutoPauseAsync(CancellationToken cancellationToken = default);
    Task SaveAutoPauseAsync(AutoPauseSettings settings, CancellationToken cancellationToken = default);
}
