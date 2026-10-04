using System;
using BTCPayServer.Abstractions;
using Xunit;

namespace BTCPayServer.Tests;

public class RequestBaseUrlTests
{
    [Theory]
    [InlineData("https://example.com", true)]
    [InlineData("http://example.com/base", true)]
    [InlineData("ftp://example.com", false)]
    [InlineData("javascript://x/%0Adocument.body.dataset.pwned=1;//", false)]
    [InlineData("file:///tmp/test", false)]
    [InlineData("https:///missing-host", false)]
    public void OnlyAcceptsWebOrigins(string value, bool expected)
    {
        Assert.Equal(expected, RequestBaseUrl.TryFromUrl(value, out _));
    }

    [Fact]
    public void UriOverloadRejectsNonWebOrigins()
    {
        Assert.Throws<FormatException>(() => RequestBaseUrl.FromUrl(new Uri("file:///tmp/test")));
    }
}
