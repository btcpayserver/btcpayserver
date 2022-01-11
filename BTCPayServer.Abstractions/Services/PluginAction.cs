using System.Threading.Tasks;
using BTCPayServer.Abstractions.Contracts;

namespace BTCPayServer.Abstractions.Services
{
    public abstract class PluginAction<T> : IPluginHookAction
    {
        public abstract string Hook { get; }
        public Task Execute(object args)
        {
            return Execute(args is T args1 ? args1 : default);
        }

        public abstract Task Execute(T arg);
    }
}
