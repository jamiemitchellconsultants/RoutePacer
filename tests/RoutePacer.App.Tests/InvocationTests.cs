using System.Text;
using RoutePacer.App.Invocation;

namespace RoutePacer.App.Tests;

public sealed class InvocationTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 27, 12, 0, 0, TimeSpan.Zero);
    private const string Token = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private const string Signature = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

    [Fact]
    public void Parser_requires_the_frozen_contract_shape()
    {
        var uri = new Uri($"https://pacetracking.tqaentry.com/open?src=RouteTimer&v=1&payload=https%3A%2F%2Fpacetracking.tqaentry.com%2Fapi%2Fhandoffs%2F{Token}&name=Caf%C3%A9&ts={Now.ToUnixTimeMilliseconds()}&sig={Signature}");
        var request = new InvocationParser().Parse(uri, Now);
        Assert.Equal("Café", request.Name);
        Assert.Equal(64, Convert.FromBase64String(request.Signature.Replace('-', '+').Replace('_', '/') + "==").Length);
    }

    [Fact]
    public void Parser_rejects_duplicate_keys_and_foreign_payload_paths()
    {
        var parser = new InvocationParser();
        var duplicate = new Uri($"https://pacetracking.tqaentry.com/open?src=RouteTimer&src=RouteTimer&v=1&payload=https%3A%2F%2Fpacetracking.tqaentry.com%2Fapi%2Fhandoffs%2F{Token}&name=&ts={Now.ToUnixTimeMilliseconds()}&sig={Signature}");
        Assert.Throws<FormatException>(() => parser.Parse(duplicate, Now));
        var wrongPath = new Uri($"https://pacetracking.tqaentry.com/open?src=RouteTimer&v=1&payload=https%3A%2F%2Fpacetracking.tqaentry.com%2Fother%2F{Token}&name=&ts={Now.ToUnixTimeMilliseconds()}&sig={Signature}");
        Assert.Throws<FormatException>(() => parser.Parse(wrongPath, Now));
    }

    [Fact]
    public void Canonicalizer_has_no_trailing_line_feed()
    {
        var request = new InvocationRequest(new Uri($"https://pacetracking.tqaentry.com/api/handoffs/{Token}"), "Café & climb", Now.ToUnixTimeMilliseconds(), Signature);
        Assert.Equal($"rt\n1\n{request.PayloadUri.AbsoluteUri}\nCafé & climb\n{request.IssuedUnixMilliseconds}", Encoding.UTF8.GetString(InvocationCanonicalizer.GetBytes(request)));
    }
}
