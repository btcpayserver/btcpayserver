using Microsoft.AspNetCore.Hosting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Text;
using NBXplorer;
using NBitcoin;
using Microsoft.AspNetCore.Http;

namespace BTCPayServer.Services
{
    public class BTCPayServerEnvironment
    {
        IHttpContextAccessor httpContext;
        public BTCPayServerEnvironment(IHostingEnvironment env, BTCPayNetworkProvider provider, IHttpContextAccessor httpContext)
        {
            this.httpContext = httpContext;
            Version = typeof(BTCPayServerEnvironment).GetTypeInfo().Assembly.GetCustomAttribute<AssemblyFileVersionAttribute>().Version;
#if DEBUG
            Build = "Debug";
#else
			Build = "Release";
#endif
            Environment = env;
            NetworkType = provider.NetworkType;
        }
        public IHostingEnvironment Environment
        {
            get; set;
        }

        public string ExpectedDomain => httpContext.HttpContext.Request.Host.Host;
        public string ExpectedHost => httpContext.HttpContext.Request.Host.Value;
        public string ExpectedProtocol => httpContext.HttpContext.Request.Scheme;

        public NetworkType NetworkType { get; set; }
        public string Version
        {
            get; set;
        }
        public string Build
        {
            get; set;
        }

        public bool IsDevelopping
        {
            get
            {
                return NetworkType == NetworkType.Regtest && Environment.IsDevelopment();
            }
        }
        public override string ToString()
        {
            StringBuilder txt = new StringBuilder();
            txt.Append($"@Copyright BTCPayServer v{Version}");
            if (!Environment.IsProduction() || !Build.Equals("Release", StringComparison.OrdinalIgnoreCase))
            {
                txt.Append($" Environment: {Environment.EnvironmentName} Build: {Build}");
            }
            return txt.ToString();
        }
    }
}
