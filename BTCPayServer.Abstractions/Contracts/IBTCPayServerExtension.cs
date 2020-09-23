using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace BTCPayServer.Contracts
{
    public interface IBTCPayServerExtension
    {
        public string Identifier { get;}
        string Name { get; }
        Version Version { get; }
        string Description { get; }

        void Execute(IApplicationBuilder applicationBuilder, IServiceProvider applicationBuilderApplicationServices);
        void Execute(IServiceCollection applicationBuilder);
    }
}
