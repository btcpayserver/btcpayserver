using System.Threading.Tasks;

namespace BTCPayServer.Contracts
{
    public abstract class PluginAction<T>:IPluginHookAction
    {
        public string Hook { get; }
        public Task Execute(object args)
        {
            return Execute(args is T args1 ? args1 : default);
        }

        public abstract Task Execute(T arg);
    }
}