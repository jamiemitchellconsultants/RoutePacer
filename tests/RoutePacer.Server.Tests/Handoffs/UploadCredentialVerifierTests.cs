using FluentAssertions;
using Microsoft.Extensions.Options;
using RoutePacer.Server.Handoffs;

namespace RoutePacer.Server.Tests.Handoffs;

public sealed class UploadCredentialVerifierTests
{
    private const string Credential = "s3cret-upload-credential";

    private static UploadCredentialVerifier Create(string configured = Credential)
        => new(Options.Create(new HandoffRelayOptions { UploadsEnabled = true, UploadCredential = configured }));

    [Fact]
    public void Accepts_the_exact_configured_credential()
        => Create().IsValid($"Bearer {Credential}").Should().BeTrue();

    [Fact]
    public void Accepts_a_case_insensitive_scheme()
        => Create().IsValid($"bearer {Credential}").Should().BeTrue();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Bearer")]
    [InlineData("Basic dXNlcjpwYXNz")]
    [InlineData("Token abc")]
    public void Rejects_a_missing_or_malformed_header(string? header)
        => Create().IsValid(header).Should().BeFalse();

    [Fact]
    public void Rejects_two_credentials_in_one_header()
        => Create().IsValid($"Bearer {Credential} {Credential}").Should().BeFalse();

    [Fact]
    public void Rejects_duplicated_header_values_joined_by_a_comma()
        => Create().IsValid($"Bearer {Credential}, Bearer {Credential}").Should().BeFalse();

    [Theory]
    [InlineData("wrong")]
    [InlineData("s3cret-upload-credentia")]
    [InlineData("s3cret-upload-credentiaL")]
    [InlineData("s3cret-upload-credential-longer")]
    public void Rejects_a_credential_that_differs_in_any_way(string presented)
        => Create().IsValid($"Bearer {presented}").Should().BeFalse();

    [Fact]
    public void An_empty_configured_credential_is_never_matched_by_a_bearer_value()
        => Create(configured: "").IsValid("Bearer ").Should().BeFalse();
}
