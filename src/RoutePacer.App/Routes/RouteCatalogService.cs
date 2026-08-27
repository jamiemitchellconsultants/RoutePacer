using Microsoft.AspNetCore.Components.Forms;
using RoutePacer.Core.Domain;
using RoutePacer.Core.Import;
using RoutePacer.Core.Storage;

namespace RoutePacer.App.Routes;

public sealed class RouteCatalogService(RouteImportService importer, IRouteRepository routes, IRideRepository rides, TimeProvider clock)
{
    public async Task<RouteSummary> ImportAsync(string fileName, string? displayName, long length, Stream content, DateTimeOffset importedAtUtc, CancellationToken cancellationToken = default)
    {
        var imported = await importer.ImportAsync(new(fileName, displayName, length, importedAtUtc), content, cancellationToken);
        await routes.SaveAsync(imported.Track, cancellationToken);
        return imported.Track.Summary;
    }

    public async Task<RouteSummary> ImportAsync(IBrowserFile file, string? displayName, CancellationToken cancellationToken = default)
    {
        await using var content = file.OpenReadStream(RouteImportLimits.MaximumFileBytes, cancellationToken);
        return await ImportAsync(file.Name, displayName, file.Size, content, clock.GetUtcNow(), cancellationToken);
    }

    /// <summary>
    /// Deleting a route would orphan the rides that reference it, so the rides must go first. Returns false
    /// without touching storage when any ride still references the route.
    /// </summary>
    public async Task<bool> TryDeleteAsync(Guid routeId, CancellationToken cancellationToken = default)
    {
        if ((await rides.ListAsync(cancellationToken)).Any(ride => ride.RouteId == routeId)) return false;
        await routes.DeleteAsync(routeId, cancellationToken);
        return true;
    }
}
