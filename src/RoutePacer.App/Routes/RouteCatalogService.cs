using Microsoft.AspNetCore.Components.Forms;
using RoutePacer.Core.Domain;
using RoutePacer.Core.Import;
using RoutePacer.Core.Storage;

namespace RoutePacer.App.Routes;

public sealed class RouteCatalogService(RouteImportService importer, IRouteRepository routes)
{
    public async Task<RouteSummary> ImportAsync(string fileName, string? displayName, long length, Stream content, DateTimeOffset importedAtUtc, CancellationToken cancellationToken = default)
    {
        var imported = await importer.ImportAsync(new(fileName, displayName, length, importedAtUtc), content, cancellationToken);
        await routes.SaveAsync(imported.Track, cancellationToken);
        return imported.Track.Summary;
    }

    public Task<RouteSummary> ImportAsync(IBrowserFile file, string? displayName, CancellationToken cancellationToken = default) => ImportAsync(file.Name, displayName, file.Size, file.OpenReadStream(52_428_800, cancellationToken), DateTimeOffset.UtcNow, cancellationToken);
}
