using Microsoft.AspNetCore.Hosting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Text;
using NBXplorer;

namespace BTCPayServer.Services
{
    public class BTCPayServerEnvironment
    {
        public BTCPayServerEnvironment(IHostingEnvironment env, BTCPayNetworkProvider provider)
        {
            Version = typeof(BTCPayServerEnvironment).GetTypeInfo().Assembly.GetCustomAttribute<AssemblyFileVersionAttribute>().Version;
#if DEBUG
            Build = "Debug";
#else
			Build = "Release";
#endif
            Environment = env;
            ChainType = provider.NBXplorerNetworkProvider.ChainType;
        }
        public IHostingEnvironment Environment
        {
            get; set;
        }
        public ChainType ChainType { get; set; }
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
                return ChainType == ChainType.Regtest && Environment.IsDevelopment();
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
