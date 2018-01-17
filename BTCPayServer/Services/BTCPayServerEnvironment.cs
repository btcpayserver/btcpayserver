using Microsoft.AspNetCore.Hosting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Text;

namespace BTCPayServer.Services
{
    public class BTCPayServerEnvironment
    {
        public BTCPayServerEnvironment(IHostingEnvironment env)
        {
            Version = typeof(BTCPayServerEnvironment).GetTypeInfo().Assembly.GetCustomAttribute<AssemblyFileVersionAttribute>().Version;
#if DEBUG
            Build = "Debug";
#else
			Build = "Release";
#endif
            Environment = env;
        }
        public IHostingEnvironment Environment
        {
            get; set;
        }
        public string Version
        {
            get; set;
        }
        public string Build
        {
            get; set;
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
