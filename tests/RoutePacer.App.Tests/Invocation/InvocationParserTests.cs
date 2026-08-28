using System.Text;
using FluentAssertions;
using RoutePacer.App.Invocation;

namespace RoutePacer.App.Tests.Invocation;

public sealed class InvocationParserTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 27, 12, 0, 0, TimeSpan.Zero);
    private const string Token = "9Xq2mWbTf4LpN0sRvEjKcYzA7dHuG1iObQ3xVn5MtSw";
    private const string PayloadUrl = "https://pacetracking.tqaentry.com/api/handoffs/" + Token;
    private static readonly string Signature = new('A', 86);

    private readonly InvocationParser _parser = new();

    private static string Url(string? src = "rt", string? v = "1", string? payload = PayloadUrl,
        string? name = "", long? ts = null, string? sig = null, string? extra = null, string? path = "/open")
    {
        var parts = new List<string>();
        if (src is not null) parts.Add($"src={Uri.EscapeDataString(src)}");
        if (v is not null) parts.Add($"v={Uri.EscapeDataString(v)}");
        if (payload is not null) parts.Add($"payload={Uri.EscapeDataString(payload)}");
        if (name is not null) parts.Add($"name={Uri.EscapeDataString(name)}");
        parts.Add($"ts={ts ?? Now.ToUnixTimeMilliseconds()}");
        parts.Add($"sig={sig ?? Signature}");
        if (extra is not null) parts.Add(extra);
        return $"https://pacetracking.tqaentry.com{path}?{string.Join('&', parts)}";
    }

    private void Reject(string url, DateTimeOffset? now = null)
        => _parser.Invoking(p => p.Parse(new Uri(url), now ?? Now)).Should().Throw<FormatException>();

    [Fact]
    public void Accepts_the_frozen_contract_shape()
    {
        var request = _parser.Parse(new Uri(Url(name: "Café & climb")), Now);

        request.PayloadUri.AbsoluteUri.Should().Be(PayloadUrl);
        request.Name.Should().Be("Café & climb");
        request.IssuedUnixMilliseconds.Should().Be(Now.ToUnixTimeMilliseconds());
    }

    [Fact]
    public void Accepts_an_empty_name() => _parser.Parse(new Uri(Url(name: "")), Now).Name.Should().BeEmpty();

    [Theory]
    [InlineData("Café & climb")]
    [InlineData("Col du Tourmalet — 100%")]
    [InlineData("a/b?c#d")]
    public void Round_trips_unicode_and_reserved_characters_in_the_name(string name)
        => _parser.Parse(new Uri(Url(name: name)), Now).Name.Should().Be(name);

    [Fact]
    public void Rejects_each_missing_key()
    {
        Reject(Url(src: null));
        Reject(Url(v: null));
        Reject(Url(payload: null));
        Reject(Url(name: null));
        Reject("https://pacetracking.tqaentry.com/open?src=rt&v=1&payload=" + Uri.EscapeDataString(PayloadUrl) + "&name=");
    }

    [Theory]
    [InlineData("src=rt")]
    [InlineData("v=1")]
    [InlineData("name=other")]
    public void Rejects_a_duplicated_key(string duplicate) => Reject(Url(extra: duplicate));

    [Fact]
    public void Rejects_an_additional_key() => Reject(Url(extra: "utm_source=qr"));

    [Theory]
    [InlineData("RT")]
    [InlineData("Rt")]
    [InlineData("rt ")]
    [InlineData(" rt")]
    [InlineData("routetimer")]
    [InlineData("Strava")]
    [InlineData("")]
    public void Rejects_a_wrong_source(string src) => Reject(Url(src: src));

    [Theory]
    [InlineData("2")]
    [InlineData("1.0")]
    [InlineData("")]
    public void Rejects_a_wrong_version(string v) => Reject(Url(v: v));

    [Fact]
    public void A_stray_percent_is_normalized_by_Uri_and_read_as_a_literal()
    {
        // System.Uri escapes a percent that does not begin a valid triplet, so it reaches the parser as "%zz".
        var url = $"https://pacetracking.tqaentry.com/open?src=rt&v=1&payload={Uri.EscapeDataString(PayloadUrl)}&name=%zz&ts={Now.ToUnixTimeMilliseconds()}&sig={Signature}";

        _parser.Parse(new Uri(url), Now).Name.Should().Be("%zz");
    }

    [Theory]
    [InlineData("http://pacetracking.tqaentry.com/api/handoffs/9Xq2mWbTf4LpN0sRvEjKcYzA7dHuG1iObQ3xVn5MtSw")]
    [InlineData("https://evil.example.com/api/handoffs/9Xq2mWbTf4LpN0sRvEjKcYzA7dHuG1iObQ3xVn5MtSw")]
    [InlineData("https://user@pacetracking.tqaentry.com/api/handoffs/9Xq2mWbTf4LpN0sRvEjKcYzA7dHuG1iObQ3xVn5MtSw")]
    [InlineData("https://pacetracking.tqaentry.com:8443/api/handoffs/9Xq2mWbTf4LpN0sRvEjKcYzA7dHuG1iObQ3xVn5MtSw")]
    [InlineData("https://pacetracking.tqaentry.com/other/9Xq2mWbTf4LpN0sRvEjKcYzA7dHuG1iObQ3xVn5MtSw")]
    [InlineData("https://pacetracking.tqaentry.com/api/handoffs/9Xq2mWbTf4LpN0sRvEjKcYzA7dHuG1iObQ3xVn5MtSw?x=1")]
    [InlineData("https://pacetracking.tqaentry.com/api/handoffs/9Xq2mWbTf4LpN0sRvEjKcYzA7dHuG1iObQ3xVn5MtSw#f")]
    [InlineData("https://pacetracking.tqaentry.com/api/handoffs/short")]
    [InlineData("https://pacetracking.tqaentry.com/api/handoffs/9Xq2mWbTf4LpN0sRvEjKcYzA7dHuG1iObQ3xVn5MtSw=")]
    public void Rejects_a_payload_url_outside_the_allowlist(string payload) => Reject(Url(payload: payload));

    [Theory]
    [InlineData("http://pacetracking.tqaentry.com/open")]
    [InlineData("https://evil.example.com/open")]
    [InlineData("https://pacetracking.tqaentry.com/opened")]
    public void Rejects_a_foreign_or_insecure_invocation_url(string prefix)
        => Reject($"{prefix}?src=rt&v=1&payload={Uri.EscapeDataString(PayloadUrl)}&name=&ts={Now.ToUnixTimeMilliseconds()}&sig={Signature}");

    [Fact]
    public void Rejects_a_fragment_on_the_invocation_url() => Reject(Url() + "#fragment");

    [Theory]
    [InlineData("not-a-number")]
    [InlineData("-1")]
    [InlineData("1.5")]
    public void Rejects_an_invalid_timestamp(string ts)
        => Reject($"https://pacetracking.tqaentry.com/open?src=rt&v=1&payload={Uri.EscapeDataString(PayloadUrl)}&name=&ts={ts}&sig={Signature}");

    [Theory]
    [InlineData("AAAA")]
    [InlineData("!!!!")]
    public void Rejects_an_invalid_signature_encoding_or_length(string sig) => Reject(Url(sig: sig));

    [Fact]
    public void Rejects_a_padded_signature() => Reject(Url(sig: new string('A', 84) + "=="));

    [Fact]
    public void Accepts_a_link_just_inside_the_ten_minute_past_bound()
        => _parser.Parse(new Uri(Url(ts: Now.AddMinutes(-10).AddSeconds(1).ToUnixTimeMilliseconds())), Now).Should().NotBeNull();

    [Fact]
    public void Rejects_a_link_older_than_ten_minutes()
        => Reject(Url(ts: Now.AddMinutes(-10).AddSeconds(-1).ToUnixTimeMilliseconds()));

    [Fact]
    public void Accepts_a_link_just_inside_the_sixty_second_future_bound()
        => _parser.Parse(new Uri(Url(ts: Now.AddSeconds(59).ToUnixTimeMilliseconds())), Now).Should().NotBeNull();

    [Fact]
    public void Rejects_a_link_further_than_sixty_seconds_in_the_future()
        => Reject(Url(ts: Now.AddSeconds(61).ToUnixTimeMilliseconds()));

    [Fact]
    public void Canonicalizer_has_line_feeds_between_fields_and_none_at_end()
    {
        var bytes = InvocationCanonicalizer.GetBytes(new InvocationRequest(new Uri(PayloadUrl), "Café & climb", 1787832000000, Signature));

        Encoding.UTF8.GetString(bytes).Should().Be($"rt\n1\n{PayloadUrl}\nCafé & climb\n1787832000000");
    }

    [Fact]
    public void Accepts_the_contract_source_marker()
    {
        var request = _parser.Parse(new Uri(Url(src: "rt")), Now);

        request.PayloadUri.AbsoluteUri.Should().Be(PayloadUrl);
    }

    [Fact]
    public void Rejects_the_source_marker_the_canonical_bytes_never_committed_to()
        => Reject(Url(src: "RouteTimer"));

    [Fact]
    public void Rejects_an_absent_source() => Reject(Url(src: null));

    [Fact]
    public void The_accepted_source_marker_is_the_one_the_canonical_bytes_open_with()
    {
        var request = _parser.Parse(new Uri(Url(src: "rt")), Now);

        Encoding.UTF8.GetString(InvocationCanonicalizer.GetBytes(request)).Should().StartWith("rt\n1\n");
    }
}
