using System;
using System.Reflection;
using BTCPayServer.Abstractions.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace BTCPayServer.Abstractions.Models
{
    public abstract class BaseBTCPayServerPlugin : IBTCPayServerPlugin
    {
        public abstract string Identifier { get; }
        public abstract string Name { get; }

        public virtual Version Version
        {
            get
            {
                return Assembly.GetAssembly(GetType())?.GetName().Version ?? new Version(1, 0, 0, 0);
            }
        }

        public abstract string Description { get; }
        public bool SystemPlugin { get; set; }
        public bool SystemExtension { get; set; }
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
