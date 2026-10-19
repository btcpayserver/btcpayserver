#nullable enable
using System;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Plugins.Bitpay.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NBitcoin;
using Xunit;

namespace BTCPayServer.Tests;

public class BitpayAuthenticationHandlerMethodTests
{
    [Theory]
    [Trait("FastTest", "FastTest")]
    [InlineData("GET", "{\"price\":1,\"currency\":\"USD\"}")]
    [InlineData("GET", "\uFEFF")]
    [InlineData("HEAD", "{\"price\":1,\"currency\":\"USD\"}")]
    [InlineData("HEAD", "\uFEFF")]
    public async Task RejectsBodyBearingReadRequests(string method, string postBody)
    {
        const string url = "https://merchant.example/invoices";
        var key = new Key();
        var signature = NBitpayClient.Extensions.BitIdExtensions.GetBitIDSignature(key, url, postBody);
        var bodyBytes = Encoding.UTF8.GetBytes(postBody);

        var context = new DefaultHttpContext();
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("merchant.example");
        context.Request.Path = "/invoices";
        context.Request.Method = method;
        context.Request.ContentType = "application/json";
        context.Request.ContentLength = bodyBytes.Length;
        context.Request.Body = new MemoryStream(bodyBytes);
        context.Request.Headers["x-signature"] = signature;
        context.Request.Headers["x-identity"] = key.PubKey.ToHex();

        var handler = new BitpayAuthenticationHandler(
            new StaticOptionsMonitor<BitpayAuthenticationOptions>(new BitpayAuthenticationOptions()),
            NullLoggerFactory.Instance,
            UrlEncoder.Default);
        await handler.InitializeAsync(
            new AuthenticationScheme(AuthenticationSchemes.Bitpay, null, typeof(BitpayAuthenticationHandler)),
            context);

        var result = await handler.AuthenticateAsync();

        Assert.False(result.Succeeded);
    }

    private sealed class StaticOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;
        public T Get(string? name) => value;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
