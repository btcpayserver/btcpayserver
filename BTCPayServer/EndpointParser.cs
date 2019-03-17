using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using BTCPayServer.Tor;

namespace BTCPayServer
{
    public static class EndPointParser
    {
        public static bool TryParse(string hostPort, out EndPoint endpoint)
        {
            if (hostPort == null)
                throw new ArgumentNullException(nameof(hostPort));
            endpoint = null;
            var index = hostPort.LastIndexOf(':');
            if (index == -1)
                return false;
            var portStr = hostPort.Substring(index + 1);
            if (!ushort.TryParse(portStr, out var port))
                return false;
            return TryParse(hostPort.Substring(0, index), port, out endpoint);
        }
        public static bool TryParse(string host, int port, out EndPoint endpoint)
        {
            if (host == null)
                throw new ArgumentNullException(nameof(host));
            endpoint = null;
            if (IPAddress.TryParse(host, out var address))
                endpoint = new IPEndPoint(address, port);
            else if (host.EndsWith(".onion", StringComparison.OrdinalIgnoreCase))
                endpoint = new OnionEndpoint(host, port);
            else
            {
                if (Uri.CheckHostName(host) != UriHostNameType.Dns)
                    return false;
                endpoint = new DnsEndPoint(host, port);
            }
            return true;
        }
    }
}
