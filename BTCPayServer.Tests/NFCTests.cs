using System;
using System.Net;
using System.Net.Http;
using BTCPayServer.Plugins.NFC;
using Xunit;

namespace BTCPayServer.Tests;

[Trait("Fast", "Fast")]
public class NFCTests
{
    [Theory]
    [InlineData("http://127.0.0.1/withdraw")]
    [InlineData("https://169.254.169.254/latest/meta-data/")]
    [InlineData("http://10.0.0.1/withdraw")]
    [InlineData("http://[::1]/withdraw")]
    [InlineData("https://[fe80::1]/withdraw")]
    [InlineData("https://[fc00::1]/withdraw")]
    [InlineData("ftp://127.0.0.1/withdraw")]
    public void RejectsUnsafeEncodedLnurlTargets(string target)
    {
        var encoded = LNURL.LNURL.EncodeUri(new Uri(target), "withdrawRequest", true).ToString();
        var uri = LNURL.LNURL.Parse(encoded, out _);

        Assert.False(NFCExternalHttpClientFactory.TryValidateUri(uri, out var error));
        Assert.NotNull(error);
    }

    [Theory]
    [InlineData("https://example.com/withdraw")]
    [InlineData("http://93.184.216.34/withdraw")]
    [InlineData("http://exampleexampleexampleexampleexampleexampleexampleexampleexample.onion/withdraw")]
    public void AcceptsPublicAndOnionLnurlTargets(string target)
    {
        Assert.True(
            NFCExternalHttpClientFactory.TryValidateUri(new Uri(target), out var error),
            error);
    }

    [Theory]
    [InlineData("127.0.0.1", false)]
    [InlineData("169.254.169.254", false)]
    [InlineData("10.0.0.1", false)]
    [InlineData("100.64.0.1", false)]
    [InlineData("192.88.99.1", false)]
    [InlineData("192.0.2.1", false)]
    [InlineData("198.18.0.1", false)]
    [InlineData("198.51.100.1", false)]
    [InlineData("203.0.113.1", false)]
    [InlineData("224.0.0.1", false)]
    [InlineData("240.0.0.1", false)]
    [InlineData("::1", false)]
    [InlineData("fe80::1", false)]
    [InlineData("febf::1", false)]
    [InlineData("fc00::1", false)]
    [InlineData("2001:db8::1", false)]
    [InlineData("2002:7f00:1::", false)]
    [InlineData("3fff::1", false)]
    [InlineData("64:ff9b::7f00:1", false)]
    [InlineData("::ffff:127.0.0.1", false)]
    [InlineData("93.184.216.34", true)]
    [InlineData("2606:2800:220:1:248:1893:25c8:1946", true)]
    public void ClassifiesResolvedAddresses(string value, bool expected)
    {
        Assert.Equal(expected, NFCExternalHttpClientFactory.IsSafeAddress(IPAddress.Parse(value)));
    }

    [Fact]
    public void RejectsAHostnameIfAnyResolvedAddressIsUnsafe()
    {
        var addresses = new[]
        {
            IPAddress.Parse("93.184.216.34"),
            IPAddress.Loopback
        };

        var exception = Assert.Throws<HttpRequestException>(() =>
            NFCExternalHttpClientFactory.EnsureSafeAddresses("example.com", addresses));
        Assert.Contains("local or non-routable", exception.Message);
    }

    [Fact]
    public void ClearnetHandlerPinsConnectionsAndDoesNotFollowRedirectsOrUseAProxy()
    {
        using var handler = NFCExternalHttpClientFactory.CreateClearnetHandler();

        Assert.False(handler.AllowAutoRedirect);
        Assert.False(handler.UseProxy);
        Assert.NotNull(handler.ConnectCallback);
    }
}
