using System;
using System.Reflection;
using BTCPayServer.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace BTCPayServer.Models
{
    public abstract class BaseBTCPayServerExtension : IBTCPayServerExtension
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
        public bool SystemExtension { get; set; }
        public virtual string[] Dependencies { get; } = Array.Empty<string>();

        public virtual void Execute(IApplicationBuilder applicationBuilder,
            IServiceProvider applicationBuilderApplicationServices)
        {
        }

        public virtual void Execute(IServiceCollection applicationBuilder)
        {
        }
    }
}
