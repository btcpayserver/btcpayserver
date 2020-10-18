using System;
using System.Text.Json.Serialization;
using BTCPayServer.Abstractions.Converters;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace BTCPayServer.Contracts
{
    public interface IBTCPayServerExtension
    {
        public string Identifier { get; }
        string Name { get; }
        [JsonConverter(typeof(VersionConverter))]
        Version Version { get; }
        string Description { get; }
        bool SystemExtension { get; set; }
        string[] Dependencies { get; }
        void Execute(IApplicationBuilder applicationBuilder, IServiceProvider applicationBuilderApplicationServices);
        void Execute(IServiceCollection applicationBuilder);
    }
}
