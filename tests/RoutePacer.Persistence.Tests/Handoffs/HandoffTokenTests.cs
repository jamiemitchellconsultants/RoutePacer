using System.Security.Cryptography;
using FluentAssertions;
using RoutePacer.Persistence.Handoffs;

namespace RoutePacer.Persistence.Tests.Handoffs;

public sealed class HandoffTokenTests
{
    private static byte[] Decode(string value)
        => Convert.FromBase64String(value.Replace('-', '+').Replace('_', '/') + new string('=', (4 - value.Length % 4) % 4));

    [Fact]
    public void Create_returns_43_character_unpadded_base64url_and_a_32_byte_hash()
    {
        var token = HandoffToken.Create();

        token.Plaintext.Should().MatchRegex("^[A-Za-z0-9_-]{43}$");
        token.Sha256.Should().HaveCount(32);
        token.Sha256.Should().Equal(SHA256.HashData(Decode(token.Plaintext)));
    }

    [Fact]
    public void Create_produces_distinct_tokens()
    {
        var tokens = Enumerable.Range(0, 200).Select(_ => HandoffToken.Create().Plaintext).ToArray();

        tokens.Distinct().Should().HaveCount(tokens.Length);
    }

    [Fact]
    public void Hash_round_trips_a_generated_token()
    {
        var token = HandoffToken.Create();

        HandoffToken.Hash(token.Plaintext).Should().Equal(token.Sha256);
    }

    [Theory]
    [InlineData("")]
    [InlineData("too-short")]
    [InlineData("9Xq2mWbTf4LpN0sRvEjKcYzA7dHuG1iObQ3xVn5MtSww")]
    [InlineData("9Xq2mWbTf4LpN0sRvEjKcYzA7dHuG1iObQ3xVn5Mt$w")]
    [InlineData("9Xq2mWbTf4LpN0sRvEjKcYzA7dHuG1iObQ3xVn5Mt+w")]
    [InlineData("9Xq2mWbTf4LpN0sRvEjKcYzA7dHuG1iObQ3xVn5Mt/w")]
    public void Hash_rejects_anything_outside_the_exact_token_shape(string plaintext)
        => new Action(() => HandoffToken.Hash(plaintext)).Should().Throw<FormatException>();

    [Fact]
    public void The_hash_covers_the_decoded_bytes_not_the_text()
    {
        var token = HandoffToken.Create();

        HandoffToken.Hash(token.Plaintext)
            .Should().Equal(SHA256.HashData(Decode(token.Plaintext)))
            .And.NotEqual(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token.Plaintext)));
    }
}
