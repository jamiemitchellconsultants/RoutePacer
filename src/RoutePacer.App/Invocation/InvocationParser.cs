using System.Globalization;
using System.Text.RegularExpressions;

namespace RoutePacer.App.Invocation;

public sealed class InvocationParser
{
    private static readonly Regex Token = new("^[A-Za-z0-9_-]{43}$", RegexOptions.CultureInvariant);
    private static readonly HashSet<string> Keys = ["src", "v", "payload", "name", "ts", "sig"];
    public InvocationRequest Parse(Uri invocationUri, DateTimeOffset now)
    {
        if (invocationUri.Scheme != Uri.UriSchemeHttps || invocationUri.Port != 443 || !string.Equals(invocationUri.Host, "pacetracking.tqaentry.com", StringComparison.OrdinalIgnoreCase) || invocationUri.AbsolutePath != "/open" || !string.IsNullOrEmpty(invocationUri.Fragment) || invocationUri.UserInfo.Length > 0) throw new FormatException("Invalid invocation URL.");
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in invocationUri.Query.TrimStart('?').Split('&', StringSplitOptions.None))
        {
            if (pair.Length == 0) continue;
            var separator = pair.IndexOf('=');
            if (separator <= 0) throw new FormatException("Invalid invocation query.");
            var key = Decode(pair[..separator]); var value = Decode(pair[(separator + 1)..]);
            if (!Keys.Contains(key) || !values.TryAdd(key, value)) throw new FormatException("Invalid invocation query.");
        }
        if (values.Count != Keys.Count || Keys.Any(k => !values.ContainsKey(k)) || values["src"] != "rt" || values["v"] != "1" || string.IsNullOrEmpty(values["payload"]) || string.IsNullOrEmpty(values["ts"]) || string.IsNullOrEmpty(values["sig"])) throw new FormatException("Invalid invocation query.");
        if (!Uri.TryCreate(values["payload"], UriKind.Absolute, out var payload) || payload.Scheme != Uri.UriSchemeHttps || payload.Port != 443 || !string.Equals(payload.Host, invocationUri.Host, StringComparison.OrdinalIgnoreCase) || payload.UserInfo.Length > 0 || !string.IsNullOrEmpty(payload.Query) || !string.IsNullOrEmpty(payload.Fragment) || !payload.AbsolutePath.StartsWith("/api/handoffs/", StringComparison.Ordinal) || !Token.IsMatch(payload.Segments.LastOrDefault() ?? "")) throw new FormatException("Invalid payload URL.");
        if (!long.TryParse(values["ts"], NumberStyles.None, CultureInfo.InvariantCulture, out var timestamp) || !TryBase64Url(values["sig"], out var signature) || signature.Length != 64) throw new FormatException("Invalid invocation values.");
        var age = now - DateTimeOffset.FromUnixTimeMilliseconds(timestamp);
        if (age > TimeSpan.FromMinutes(10) || age < -TimeSpan.FromSeconds(60)) throw new FormatException("Invocation is outside its validity window.");
        return new InvocationRequest(payload, values["name"], timestamp, values["sig"]);
    }
    private static string Decode(string value)
    {
        for (var i = 0; i < value.Length; i++) if (value[i] == '%' && (i + 2 >= value.Length || !Uri.IsHexDigit(value[i + 1]) || !Uri.IsHexDigit(value[i + 2]))) throw new FormatException("Invalid percent encoding.");
        try { return Uri.UnescapeDataString(value); } catch (UriFormatException ex) { throw new FormatException("Invalid percent encoding.", ex); }
    }
    internal static bool TryBase64Url(string value, out byte[] bytes) { bytes = []; if (value.Length % 4 == 1 || value.Contains('=') || value.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('_' or '-'))) return false; try { bytes = Convert.FromBase64String(value.Replace('-', '+').Replace('_', '/') + new string('=', (4 - value.Length % 4) % 4)); return true; } catch (FormatException) { return false; } }
}
