using System;
using System.Linq;
using System.Reflection;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using NBitcoin;

namespace BTCPayServer.Services
{
    public class BTCPayServerEnvironment
    {
        readonly IHttpContextAccessor httpContext;
        readonly TorServices torServices;
        public BTCPayServerEnvironment(IWebHostEnvironment env, BTCPayNetworkProvider provider, IHttpContextAccessor httpContext, TorServices torServices)
        {
            this.httpContext = httpContext;
            Version = typeof(BTCPayServerEnvironment).GetTypeInfo().Assembly.GetCustomAttribute<AssemblyFileVersionAttribute>().Version;
#if DEBUG
            Build = "Debug";
#else
			Build = "Release";
#endif
#if ALTCOINS
            AltcoinsVersion = true;
#else
            AltcoinsVersion = false;
#endif

            Environment = env;
            NetworkType = provider.NetworkType;
            this.torServices = torServices;
        }
        public IWebHostEnvironment Environment
        {
            get; set;
        }

        public string ExpectedDomain => httpContext.HttpContext.Request.Host.Host;
        public string ExpectedHost => httpContext.HttpContext.Request.Host.Value;
        public string ExpectedProtocol => httpContext.HttpContext.Request.Scheme;
        public string OnionUrl => this.torServices.Services.Where(s => s.ServiceType == TorServiceType.BTCPayServer)
                                                           .Select(s => $"http://{s.OnionHost}").FirstOrDefault();

        public NetworkType NetworkType { get; set; }
        public string Version
        {
            get; set;
        }
        public string Build
        {
            get; set;
        }
        public bool AltcoinsVersion { get; set; }

        public bool IsDeveloping
        {
            get
            {
                return NetworkType == NetworkType.Regtest && Environment.IsDevelopment();
            }
        }

        public bool IsSecure
        {
            get
            {
                return NetworkType != NetworkType.Mainnet ||
                       httpContext.HttpContext.Request.Scheme == "https" ||
                       httpContext.HttpContext.Request.Host.Host.EndsWith(".onion", StringComparison.OrdinalIgnoreCase) ||
                       Extensions.IsLocalNetwork(httpContext.HttpContext.Request.Host.Host);
            }
        }

        public HttpContext Context => httpContext.HttpContext;

        public override string ToString()
        {
            StringBuilder txt = new StringBuilder();
            txt.Append($"@Copyright BTCPayServer v{Version}");
            if (AltcoinsVersion)
                txt.Append($" (altcoins)");
            if (!Environment.IsProduction() || !Build.Equals("Release", StringComparison.OrdinalIgnoreCase))
            {
                txt.Append($" Environment: {Environment.EnvironmentName} Build: {Build}");
            }
            return txt.ToString();
        }
    }
}
