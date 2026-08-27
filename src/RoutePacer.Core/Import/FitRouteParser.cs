using Dynastream.Fit;

namespace RoutePacer.Core.Import;

public sealed class FitRouteParser : IRouteFileParser
{
    public bool CanParse(string fileName) => string.Equals(Path.GetExtension(fileName), ".fit", StringComparison.OrdinalIgnoreCase);

    public Task<IReadOnlyList<RawRoutePoint>> ParseAsync(Stream content, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        cancellationToken.ThrowIfCancellationRequested();
        var points = new List<RawRoutePoint>();
        try
        {
            var decoder = new Decode();
            decoder.MesgEvent += (_, args) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (args.mesg is not RecordMesg record || record.GetPositionLat() is not { } latRaw || record.GetPositionLong() is not { } lonRaw) return;
                var lat = latRaw * (180d / 2_147_483_648d); var lon = lonRaw * (180d / 2_147_483_648d);
                points.Add(new RawRoutePoint(lat, lon, record.GetAltitude(), null, ReadTimestamp(record)));
                if (points.Count > RouteImportLimits.MaximumPoints) throw new RouteImportException("too-many-points", "The route contains too many points.");
            };
            if (!decoder.Read(content)) throw new RouteImportException("malformed-fit", "The FIT document checksum is invalid.");
            return Task.FromResult<IReadOnlyList<RawRoutePoint>>(points);
        }
        catch (RouteImportException) { throw; }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { throw new RouteImportException("malformed-fit", "The FIT document is malformed.", ex); }
    }

    // A course without timestamps is not a malformed file; it normalizes to a distance-only route.
    private static DateTimeOffset? ReadTimestamp(RecordMesg record)
    {
        if (record.GetTimestamp() is not { } timestamp) return null;
        return new DateTimeOffset(System.DateTime.SpecifyKind(timestamp.GetDateTime(), DateTimeKind.Utc));
    }
}
