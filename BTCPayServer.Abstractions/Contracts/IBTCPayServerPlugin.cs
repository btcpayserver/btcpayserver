using System;
using System.Text.Json.Serialization;
using BTCPayServer.Abstractions.Converters;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace BTCPayServer.Abstractions.Contracts
{
    public interface IBTCPayServerPlugin
    {
        public string Identifier { get; }
        string Name { get; }
        [JsonConverter(typeof(VersionConverter))]
        Version Version { get; }
        string Description { get; }
        bool SystemPlugin { get; set; }
        PluginDependency[] Dependencies { get; }
        void Execute(IApplicationBuilder applicationBuilder, IServiceProvider applicationBuilderApplicationServices);
        void Execute(IServiceCollection applicationBuilder);

        public class PluginDependency
        {
            public string Identifier { get; set; }
            public string Condition { get; set; }

            public VersionCondition ParseCondition()
            {
                VersionCondition.TryParse(Condition ?? "", out var condition);
                return condition ?? new VersionCondition.Yes();
            }

            public override string ToString()
            {
                // Format it
                var cond = Condition ?? "";
                if (VersionCondition.TryParse(cond, out var condition))
                    cond = condition.ToString();
                return $"{Identifier}: {cond}";
            }
        }
    }

}
