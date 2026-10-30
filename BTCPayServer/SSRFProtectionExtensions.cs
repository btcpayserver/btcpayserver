#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

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
            var logger = sp.GetService<ILoggerFactory>()?.CreateLogger(typeof(SSRFProtectionExtensions));
            handler.ConnectCallback = (context, cancellationToken) =>
                Connect(context, opt.SSRFExceptions, logger, cancellationToken);
        }
    });


    static async ValueTask<Stream> Connect(SocketsHttpConnectionContext context,
        IReadOnlyList<SSRFAllowedDestination> exceptions,
        ILogger? logger,
        CancellationToken cancellationToken)
    {
        var addresses = await GetAllowedAddresses(context.DnsEndPoint.Host, context.DnsEndPoint.Port,
            exceptions, logger, cancellationToken);

        Exception? lastException = null;
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

        if (lastException is null)
            throw new HttpRequestException("The endpoint does not resolve to an allowed network address");
        else
            throw new HttpRequestException("Could not connect to the endpoint", lastException);
    }

    internal static async Task<List<IPAddress>> GetAllowedAddresses(string host, int port,
        IReadOnlyList<SSRFAllowedDestination> exceptions,
        ILogger? logger,
        CancellationToken cancellationToken)
    {
        var addresses = await Dns.GetHostAddressesAsync(host, AddressFamily.Unspecified, cancellationToken);
        var hostnameException = exceptions.FirstOrDefault(exception => exception.MatchesHostname(host, port));

        var allowed = new List<IPAddress>();
        foreach (var address in addresses)
        {
            var isLocal = Extensions.IsLocalNetwork(address.ToString());
            var addressException = exceptions.FirstOrDefault(exception => exception.MatchesAddress(address, port));
            if (isLocal && hostnameException is null && addressException is null)
                continue;
            if (isLocal)
            {
                logger?.LogDebug("SSRF exception permitted connection to {Host}:{Port} at {Address}",
                    host, port, address);
            }
            allowed.Add(address);
        }
        return allowed;
    }
}
