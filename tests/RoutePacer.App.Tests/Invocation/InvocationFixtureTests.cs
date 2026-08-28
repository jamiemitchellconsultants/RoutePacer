using System.Text;
using FluentAssertions;
using RoutePacer.App.Invocation;

namespace RoutePacer.App.Tests.Invocation;

public sealed class InvocationFixtureTests
{
    private readonly ContractFixture _fixture = ContractFixture.Load();

    [Fact]
    public void The_fixture_declares_contract_version_one() => _fixture.FixtureVersion.Should().Be(1);

    [Fact]
    public void The_published_key_is_a_public_p256_jwk_with_no_private_component()
    {
        _fixture.PublicJwk.Should().Contain("\"kty\": \"EC\"").And.Contain("\"crv\": \"P-256\"");
        _fixture.PublicJwk.Should().NotContain("\"d\"");
    }

    [Fact]
    public void The_signature_is_sixty_four_bytes_of_unpadded_base64url()
    {
        _fixture.Signature.Should().MatchRegex("^[A-Za-z0-9_-]+$");
        ContractFixture.Base64Url(_fixture.Signature).Should().HaveCount(64);
    }

    [Fact]
    public void The_canonical_text_has_line_feeds_between_fields_and_none_at_the_end()
    {
        _fixture.CanonicalText.Should().Be($"rt\n1\n{_fixture.PayloadUrl}\n{_fixture.Name}\n{_fixture.Timestamp}");
        _fixture.CanonicalText.Should().NotEndWith("\n");
    }

    [Fact]
    public void The_canonicalizer_reproduces_the_fixture_bytes_exactly()
    {
        var request = new InvocationRequest(new Uri(_fixture.PayloadUrl), _fixture.Name, _fixture.Timestamp, _fixture.Signature);

        InvocationCanonicalizer.GetBytes(request).Should().Equal(Encoding.UTF8.GetBytes(_fixture.CanonicalText));
    }

    [Fact]
    public void The_fixture_signature_verifies_over_the_canonical_bytes()
    {
        var request = new InvocationParser().Parse(new Uri(_fixture.InvocationUrl), _fixture.IssuedAt.AddMinutes(1));

        _fixture.Verify(InvocationCanonicalizer.GetBytes(request), request.Signature).Should().BeTrue();
    }

    [Fact]
    public void The_parser_recovers_every_field_from_the_fixture_invocation_url()
    {
        var request = new InvocationParser().Parse(new Uri(_fixture.InvocationUrl), _fixture.IssuedAt.AddMinutes(1));

        request.PayloadUri.AbsoluteUri.Should().Be(_fixture.PayloadUrl);
        request.Name.Should().Be(_fixture.Name);
        request.IssuedUnixMilliseconds.Should().Be(_fixture.Timestamp);
        request.Signature.Should().Be(_fixture.Signature);
    }

    [Theory]
    [InlineData("tampered name")]
    [InlineData("")]
    public void A_tampered_name_breaks_verification(string name)
    {
        var tampered = new InvocationRequest(new Uri(_fixture.PayloadUrl), name, _fixture.Timestamp, _fixture.Signature);

        _fixture.Verify(InvocationCanonicalizer.GetBytes(tampered), tampered.Signature).Should().BeFalse();
    }

    [Fact]
    public void A_tampered_timestamp_breaks_verification()
    {
        var tampered = new InvocationRequest(new Uri(_fixture.PayloadUrl), _fixture.Name, _fixture.Timestamp + 1, _fixture.Signature);

        _fixture.Verify(InvocationCanonicalizer.GetBytes(tampered), tampered.Signature).Should().BeFalse();
    }

    [Fact]
    public void A_tampered_payload_url_breaks_verification()
    {
        var swapped = _fixture.PayloadUrl[..^1] + (_fixture.PayloadUrl[^1] == 'A' ? 'Q' : 'A');
        var tampered = new InvocationRequest(new Uri(swapped), _fixture.Name, _fixture.Timestamp, _fixture.Signature);

        _fixture.Verify(InvocationCanonicalizer.GetBytes(tampered), tampered.Signature).Should().BeFalse();
    }

    [Fact]
    public void A_tampered_signature_breaks_verification()
    {
        var bytes = ContractFixture.Base64Url(_fixture.Signature);
        bytes[0] ^= 0xFF;
        var tampered = Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var request = new InvocationRequest(new Uri(_fixture.PayloadUrl), _fixture.Name, _fixture.Timestamp, tampered);

        _fixture.Verify(InvocationCanonicalizer.GetBytes(request), tampered).Should().BeFalse();
    }

    [Fact]
    public void The_fixture_invocation_url_carries_the_contract_source_marker()
        => _fixture.InvocationUrl.Should().Contain("/open?src=rt&v=1&");

    [Fact]
    public void The_fixture_invocation_url_is_refused_once_its_source_marker_is_altered()
    {
        var altered = _fixture.InvocationUrl.Replace("?src=rt&", "?src=RouteTimer&");

        new InvocationParser().Invoking(p => p.Parse(new Uri(altered), _fixture.IssuedAt.AddMinutes(1)))
            .Should().Throw<FormatException>();
    }

    [Fact]
    public void The_canonical_bytes_the_parser_feeds_the_verifier_open_with_the_source_marker()
    {
        var request = new InvocationParser().Parse(new Uri(_fixture.InvocationUrl), _fixture.IssuedAt.AddMinutes(1));

        Encoding.UTF8.GetString(InvocationCanonicalizer.GetBytes(request)).Should().StartWith("rt\n1\n");
    }

    [Fact]
    public void The_fixture_is_refused_outside_its_validity_window()
    {
        var parser = new InvocationParser();

        parser.Invoking(p => p.Parse(new Uri(_fixture.InvocationUrl), _fixture.IssuedAt.AddMinutes(11)))
            .Should().Throw<FormatException>();
    }
}
