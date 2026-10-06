using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BTCPayServer;

public static class SSRFProtectionExtensions
{
    public static IHttpClientBuilder UseSSRFProtection(this IHttpClientBuilder builder)
    => builder.ConfigurePrimaryHttpMessageHandler((h, sp) =>
    {
        var handler = (SocketsHttpHandler)h;
        var opt = sp.GetRequiredService<BTCPayServerOptions>();
        if (!opt.DisableSSRFProtection)
        {
            handler.UseProxy = false;
            handler.ConnectCallback = Connect;
        }
    });


    static async ValueTask<Stream> Connect(SocketsHttpConnectionContext context,
        CancellationToken cancellationToken)
    {
        var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host,
            AddressFamily.Unspecified, cancellationToken);
        if (addresses.Length is 0 ||
            addresses.Any(a => BTCPayServer.Extensions.IsLocalNetwork(a.ToString())))
        {
            throw new HttpRequestException("The endpoint does not resolve exclusively to public addresses");
        }

        Exception lastException = null;
        foreach (var address in addresses)
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            try
            {
                await socket.ConnectAsync(new IPEndPoint(address, context.DnsEndPoint.Port), cancellationToken);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                socket.Dispose();
                lastException = ex;
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }

        throw new HttpRequestException("Could not connect to the endpoint", lastException);
    }
}
