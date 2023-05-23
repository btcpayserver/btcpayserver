using System;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text;
using BTCPayServer.Configuration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NBitcoin;

namespace BTCPayServer.Services
{
    public class BTCPayServerEnvironment
    {
        readonly TorServices torServices;
        public BTCPayServerEnvironment(IWebHostEnvironment env, BTCPayNetworkProvider provider, TorServices torServices, BTCPayServerOptions opts)
        {
            Version = GetInformationalVersion();
            Commit = typeof(BTCPayServerEnvironment).GetTypeInfo().Assembly.GetCustomAttribute<GitCommitAttribute>()?.ShortSHA;
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
            CheatMode = opts.CheatMode;
        }

        internal static string GetInformationalVersion()
        {
            return typeof(BTCPayServerEnvironment).GetTypeInfo().Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>().InformationalVersion;
        }

        public IWebHostEnvironment Environment
        {
            get; set;
        }

        public string OnionUrl => this.torServices.Services.Where(s => s.ServiceType == TorServiceType.BTCPayServer)
                                                           .Select(s => $"http://{s.OnionHost}").FirstOrDefault();

        public bool CheatMode { get; set; }
        public ChainName NetworkType { get; set; }
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
                return NetworkType == ChainName.Regtest && Environment.IsDevelopment();
            }
        }

        public bool IsSecure(HttpContext httpContext)
        {
            return NetworkType != ChainName.Mainnet ||
                       httpContext.Request.Scheme == "https" ||
                       httpContext.Request.Host.Host.EndsWith(".onion", StringComparison.OrdinalIgnoreCase) ||
                       Extensions.IsLocalNetwork(httpContext.Request.Host.Host);
        }

        public string Commit { get; set; }

        public override string ToString()
        {
            StringBuilder txt = new StringBuilder();
            txt.Append(CultureInfo.InvariantCulture, $"© BTCPay Server v{Version}");
            if (Commit != null)
                txt.Append($"+{Commit}");
            if (AltcoinsVersion)
                txt.Append(" (Altcoins)");
            if (!Environment.IsProduction() || !Build.Equals("Release", StringComparison.OrdinalIgnoreCase))
            {
                txt.Append(CultureInfo.InvariantCulture, $" Environment: {Environment.EnvironmentName} ({Build})");
            }
            return txt.ToString();
        }
    }
}
