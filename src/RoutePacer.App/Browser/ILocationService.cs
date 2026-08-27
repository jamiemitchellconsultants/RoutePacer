using RoutePacer.Core.Domain;

namespace RoutePacer.App.Browser;

public enum LocationFailure { PermissionDenied, Unavailable, Timeout, Unsupported, Unknown }
public interface ILocationService : IAsyncDisposable
{
    Task StartAsync(Func<GeoFix, Task> onFix, Func<LocationFailure, Task> onError, CancellationToken cancellationToken = default);
    Task StopAsync();
}
