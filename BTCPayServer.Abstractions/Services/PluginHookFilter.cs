using System.Threading.Tasks;
using BTCPayServer.Abstractions.Contracts;

namespace BTCPayServer.Abstractions.Services
{
    public abstract class PluginHookFilter<T> : IPluginHookFilter
    {
        public abstract string Hook { get; }

        public Task<object> Execute(object args)
        {
            return Execute(args is T args1 ? args1 : default).ContinueWith(task => task.Result as object);
        }

        public abstract Task<T> Execute(T arg);
    }
}
