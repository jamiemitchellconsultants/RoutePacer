using Microsoft.AspNetCore.Components.Forms;
using RoutePacer.Core.Domain;
using RoutePacer.Core.Import;
using RoutePacer.Core.Storage;

namespace RoutePacer.App.Routes;

/// <summary>
/// The application holds one route. Importing replaces it, and the replacement is what
/// <see cref="IRouteRepository.SaveAsync"/> performs atomically -- there is no delete-then-import
/// window in which the rider has no route at all.
/// </summary>
public sealed class RouteCatalogService(RouteImportService importer, IRouteRepository routes, TimeProvider clock)
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

    public Task<RouteTrack?> GetAsync(CancellationToken cancellationToken = default) => routes.GetAsync(cancellationToken);

    public Task ClearAsync(CancellationToken cancellationToken = default) => routes.ClearAsync(cancellationToken);
}
