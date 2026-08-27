using RoutePacer.Core.Domain;

namespace RoutePacer.Core.Import;

public sealed record RouteImportRequest(string FileName, string? DisplayName, long Length, DateTimeOffset ImportedAtUtc);
public sealed record ImportedRoute(RouteTrack Track, string OriginalFileName);
