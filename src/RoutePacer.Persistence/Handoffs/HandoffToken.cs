using System.Security.Cryptography;

namespace RoutePacer.Persistence.Handoffs;

public sealed record HandoffToken(string Plaintext, byte[] Sha256)
{
    public static HandoffToken Create()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return new HandoffToken(ToBase64Url(bytes), SHA256.HashData(bytes));
    }

    public static byte[] Hash(string plaintext)
    {
        if (plaintext.Length != 43 || plaintext.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('_' or '-'))) throw new FormatException("Invalid handoff token.");
        var bytes = Convert.FromBase64String(plaintext.Replace('-', '+').Replace('_', '/') + "=");
        if (bytes.Length != 32) throw new FormatException("Invalid handoff token.");
        return SHA256.HashData(bytes);
    }

    private static string ToBase64Url(ReadOnlySpan<byte> bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
