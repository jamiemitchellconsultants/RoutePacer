using System.Globalization;
using System.Xml;

namespace RoutePacer.Core.Import;

public sealed class GpxRouteParser : IRouteFileParser
{
    public const int MaximumPoints = 250_000;
    public bool CanParse(string fileName) => string.Equals(Path.GetExtension(fileName), ".gpx", StringComparison.OrdinalIgnoreCase);

    public async Task<IReadOnlyList<RawRoutePoint>> ParseAsync(Stream content, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        var settings = new XmlReaderSettings { Async = true, DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 75_000_000, IgnoreComments = true, IgnoreWhitespace = true };
        var result = new List<RawRoutePoint>();
        try
        {
            using var reader = XmlReader.Create(content, settings);
            while (await reader.ReadAsync().ConfigureAwait(false))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (reader.NodeType != XmlNodeType.Element || (reader.LocalName is not ("trkpt" or "rtept"))) continue;
                var latText = reader.GetAttribute("lat"); var lonText = reader.GetAttribute("lon");
                if (!double.TryParse(latText, NumberStyles.Float, CultureInfo.InvariantCulture, out var lat) || !double.TryParse(lonText, NumberStyles.Float, CultureInfo.InvariantCulture, out var lon))
                    throw new RouteImportException("invalid-gpx-value", "A GPX coordinate is invalid.");
                double? ele = null; DateTimeOffset? timestamp = null;
                using var subtree = reader.ReadSubtree();
                while (await subtree.ReadAsync().ConfigureAwait(false))
                {
                    if (subtree.NodeType != XmlNodeType.Element) continue;
                    if (subtree.LocalName == "ele")
                    {
                        var value = await subtree.ReadElementContentAsStringAsync().ConfigureAwait(false);
                        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)) throw new RouteImportException("invalid-gpx-value", "A GPX elevation is invalid.");
                        ele = parsed;
                    }
                    else if (subtree.LocalName == "time")
                    {
                        var value = await subtree.ReadElementContentAsStringAsync().ConfigureAwait(false);
                        if (!DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)) throw new RouteImportException("invalid-gpx-value", "A GPX timestamp is invalid.");
                        timestamp = parsed.ToUniversalTime();
                    }
                }
                result.Add(new RawRoutePoint(lat, lon, ele, null, timestamp));
                if (result.Count > MaximumPoints) throw new RouteImportException("too-many-points", "The route contains too many points.");
            }
            return result;
        }
        catch (RouteImportException) { throw; }
        catch (OperationCanceledException) { throw; }
        catch (XmlException ex) { throw new RouteImportException("malformed-gpx", "The GPX document is malformed.", ex); }
        catch (Exception ex) when (ex is FormatException or InvalidOperationException) { throw new RouteImportException("invalid-gpx-value", "The GPX contains an invalid value.", ex); }
    }
}
