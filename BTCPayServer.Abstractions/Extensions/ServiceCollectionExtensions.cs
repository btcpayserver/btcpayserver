using BTCPayServer.Abstractions.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace BTCPayServer.Abstractions.Extensions
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddStartupTask<T>(this IServiceCollection services)
            where T : class, IStartupTask
            => services.AddTransient<IStartupTask, T>();
    }
}
