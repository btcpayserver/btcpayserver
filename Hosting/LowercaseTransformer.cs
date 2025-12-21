using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace BTCPayServer.Hosting
{
    public class LowercaseTransformer : IOutboundParameterTransformer
    {
        public static void Register(IServiceCollection services)
        {
            services.AddRouting(opts =>
            {
                opts.ConstraintMap["lowercase"] = typeof(LowercaseTransformer);
            });
        }

        public string TransformOutbound(object value)
        {
            if (value is not string str)
                return null;
            return str.ToLowerInvariant();
        }
    }
}
