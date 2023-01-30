using System;
using System.Reflection;
using BTCPayServer.Abstractions.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace BTCPayServer.Abstractions.Models
{
    public abstract class BaseBTCPayServerPlugin : IBTCPayServerPlugin
    {
        public virtual string Identifier
        {
            get
            {
                return GetType().GetTypeInfo().Assembly.GetName().Name;
            }
        }
        public virtual string Name
        {
            get
            {
                return GetType().GetTypeInfo().Assembly
                        .GetCustomAttribute<AssemblyProductAttribute>()?
                        .Product ?? "???";
            }
        }

        public virtual Version Version
        {
            get
            {
                return GetVersion(GetType().GetTypeInfo().Assembly
                        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                        .InformationalVersion) ??
                        Assembly.GetAssembly(GetType())?.GetName()?.Version ??
                        new Version(1, 0, 0, 0);
            }
        }

        private static Version GetVersion(string informationalVersion)
        {
            if (informationalVersion is null)
                return null;
            Version.TryParse(informationalVersion, out var r);
            return r;
        }

        public virtual string Description
        {
            get
            {
                return GetType().GetTypeInfo().Assembly
                        .GetCustomAttribute<AssemblyDescriptionAttribute>()?
                        .Description ?? string.Empty;
            }
        }
        public bool SystemPlugin { get; set; }
        public virtual IBTCPayServerPlugin.PluginDependency[] Dependencies { get; } = Array.Empty<IBTCPayServerPlugin.PluginDependency>();

        public virtual void Execute(IApplicationBuilder applicationBuilder,
            IServiceProvider applicationBuilderApplicationServices)
        {
        }

        public virtual void Execute(IServiceCollection applicationBuilder)
        {
        }
    }
}
