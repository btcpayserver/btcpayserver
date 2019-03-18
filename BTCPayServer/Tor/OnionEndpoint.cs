using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace BTCPayServer.Tor
{
    public class OnionEndpoint : DnsEndPoint
    {
        public OnionEndpoint(string host, int port): base(host, port)
        {

        }
    }
}
