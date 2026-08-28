using System.Security.Cryptography;
using System.Text.Json;

namespace RoutePacer.App.Tests.Invocation;

/// <summary>
/// The frozen Contract v1 vector, mirrored byte-for-byte from RouteTimer's copy at
/// tests/RouteTimer.Services.Tests/RoutePacer/Fixtures/routepacer-contract-v1.json.
/// Its key pair is a published test key and must never be used in a deployed environment.
/// </summary>
public sealed class ContractFixture
{
    public static ContractFixture Load()
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "route-timer-contract-v1.json")));
        var root = document.RootElement;
        return new ContractFixture
        {
            FixtureVersion = root.GetProperty("version").GetInt32(),
            PublicJwk = root.GetProperty("publicJwk").GetRawText(),
            CanonicalText = root.GetProperty("canonical").GetString()!,
            PayloadUrl = root.GetProperty("payloadUrl").GetString()!,
            Name = root.GetProperty("name").GetString()!,
            Timestamp = root.GetProperty("issuedUnixMilliseconds").GetInt64(),
            Signature = root.GetProperty("signature").GetString()!,
            InvocationUrl = root.GetProperty("invocationUrl").GetString()!,
        };
    }

    public int FixtureVersion { get; private init; }
    public string PublicJwk { get; private init; } = "";
    public string CanonicalText { get; private init; } = "";
    public string PayloadUrl { get; private init; } = "";
    public string Name { get; private init; } = "";
    public long Timestamp { get; private init; }
    public string Signature { get; private init; } = "";
    public string InvocationUrl { get; private init; } = "";

    public DateTimeOffset IssuedAt => DateTimeOffset.FromUnixTimeMilliseconds(Timestamp);

    public static byte[] Base64Url(string value)
        => Convert.FromBase64String(value.Replace('-', '+').Replace('_', '/') + new string('=', (4 - value.Length % 4) % 4));

    /// <summary>Verifies with .NET rather than Web Crypto, which is unavailable outside a browser.</summary>
    public bool Verify(byte[] canonicalBytes, string signature)
    {
        using var document = JsonDocument.Parse(PublicJwk);
        var root = document.RootElement;
        using var key = ECDsa.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint { X = Base64Url(root.GetProperty("x").GetString()!), Y = Base64Url(root.GetProperty("y").GetString()!) }
        });
        return key.VerifyData(canonicalBytes, Base64Url(signature), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
    }
}
