using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace BTCPayServer.Abstractions.Extensions
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddStartupTask<T>(this IServiceCollection services)
            where T : class, IStartupTask
            => services.AddTransient<IStartupTask, T>();

        /// <summary>
        /// Applies a regex to every attribute-route parameter with the specified name.
        /// </summary>
        public static IServiceCollection AddRegexRouteConvention(this IServiceCollection services,
            string routeParameterName, string pattern)
        {
            var convention = new RegexRouteConvention(routeParameterName, pattern);
            return services.AddSingleton(convention);
        }
    }
}
