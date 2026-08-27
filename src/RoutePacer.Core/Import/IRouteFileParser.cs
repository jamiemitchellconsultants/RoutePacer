namespace RoutePacer.Core.Import;

public interface IRouteFileParser
{
    bool CanParse(string fileName);
    Task<IReadOnlyList<RawRoutePoint>> ParseAsync(Stream content, CancellationToken cancellationToken = default);
}
