using System;

namespace BTCPayServer.Services.Altcoins.Monero.Services
{
    public class DaemonParams
    {
        public Uri address { get; set; }
        public string username { get; set; }
        public string password { get; set; }
        public bool trusted { get; set; }
        public string sslSupport { get; set; }
    }
}
