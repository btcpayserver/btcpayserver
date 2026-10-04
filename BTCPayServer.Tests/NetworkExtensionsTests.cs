using Xunit;

namespace BTCPayServer.Tests;

public class NetworkExtensionsTests
{
    [Theory]
    [InlineData("localhost.", true)]
    [InlineData("server.internal.", true)]
    [InlineData("example.com.", false)]
    [InlineData("0.0.0.0", true)]
    [InlineData("10.0.0.1", true)]
    [InlineData("100.64.0.1", true)]
    [InlineData("127.0.0.1", true)]
    [InlineData("169.254.0.1", true)]
    [InlineData("172.16.0.1", true)]
    [InlineData("192.0.0.1", true)]
    [InlineData("192.0.2.1", true)]
    [InlineData("192.168.0.1", true)]
    [InlineData("198.18.0.1", true)]
    [InlineData("198.51.100.1", true)]
    [InlineData("203.0.113.1", true)]
    [InlineData("224.0.0.1", true)]
    [InlineData("8.8.8.8", false)]
    [InlineData("::", true)]
    [InlineData("::1", true)]
    [InlineData("::ffff:10.0.0.1", true)]
    [InlineData("2001:2::1", true)]
    [InlineData("2001:db8::1", true)]
    [InlineData("3fff:fff::1", true)]
    [InlineData("fc00::1", true)]
    [InlineData("fe80::1", true)]
    [InlineData("ff00::1", true)]
    [InlineData("2001:4860:4860::8888", false)]
    public void DetectsInternalNetworkAddresses(string server, bool expected)
    {
        Assert.Equal(expected, BTCPayServer.Extensions.IsLocalNetwork(server));
    }
}
