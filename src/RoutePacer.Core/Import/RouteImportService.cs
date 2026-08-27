using RoutePacer.Core.Domain;

namespace RoutePacer.Core.Import;

public sealed class RouteImportService(IReadOnlyList<IRouteFileParser> parsers, RouteNormalizer normalizer)
{
    public async Task<ImportedRoute> ImportAsync(RouteImportRequest request, Stream content, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request); ArgumentNullException.ThrowIfNull(content);
        if (request.Length <= 0 || request.Length > 52_428_800) throw new RouteImportException("file-too-large", "The route file must be between 1 byte and 50 MB.");
        var parser = parsers.SingleOrDefault(p => p.CanParse(request.FileName)) ?? throw new RouteImportException("unsupported-file", "Only GPX and FIT files are supported.");
        var points = await parser.ParseAsync(content, cancellationToken).ConfigureAwait(false);
        var stem = Path.GetFileNameWithoutExtension(request.FileName);
        var displayName = string.IsNullOrWhiteSpace(request.DisplayName) ? (string.IsNullOrWhiteSpace(stem) ? "Imported route" : stem) : request.DisplayName.Trim();
        var source = string.Equals(Path.GetExtension(request.FileName), ".fit", StringComparison.OrdinalIgnoreCase) ? RouteSourceType.Fit : RouteSourceType.Gpx;
        return new ImportedRoute(normalizer.Normalize(Guid.NewGuid(), displayName, source, request.ImportedAtUtc, points), request.FileName);
    }
}
