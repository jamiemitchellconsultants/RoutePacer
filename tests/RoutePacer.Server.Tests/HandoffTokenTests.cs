using FluentAssertions;
using RoutePacer.Persistence.Handoffs;
namespace RoutePacer.Server.Tests;

public sealed class HandoffTokenTests
{
    [Fact]
    public void Token_is_unpadded_base64url_and_hash_round_trips()
    {
        var token = HandoffToken.Create();
        token.Plaintext.Should().MatchRegex("^[A-Za-z0-9_-]{43}$");
        HandoffToken.Hash(token.Plaintext).Should().Equal(token.Sha256);
    }
}
