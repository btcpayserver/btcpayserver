using System.Net;
using System.Net.Http;
using BTCPayServer.HostedServices;

namespace BTCPayServer.Services
{
    public class Socks5HttpClientHandler : HttpClientHandler
    {
        public Socks5HttpClientHandler(Socks5HttpProxyServer sock5)
        {
            this.Proxy = new WebProxy(sock5.Uri);
        }
    }
}
