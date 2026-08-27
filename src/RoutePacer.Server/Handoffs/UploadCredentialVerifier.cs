using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace RoutePacer.Server.Handoffs;

public sealed class UploadCredentialVerifier(IOptions<HandoffRelayOptions> options)
{
    public bool IsValid(string? authorizationHeader)
    {
        if (string.IsNullOrWhiteSpace(authorizationHeader)) return false;
        var parts = authorizationHeader.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2 || !parts[0].Equals("Bearer", StringComparison.OrdinalIgnoreCase)) return false;
        var configured = SHA256.HashData(Encoding.UTF8.GetBytes(options.Value.UploadCredential));
        var presented = SHA256.HashData(Encoding.UTF8.GetBytes(parts[1]));
        return CryptographicOperations.FixedTimeEquals(configured, presented);
    }
}
