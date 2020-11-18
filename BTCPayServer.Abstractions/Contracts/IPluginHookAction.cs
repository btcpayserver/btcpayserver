using System.Threading.Tasks;

namespace BTCPayServer.Contracts
{
    public interface IPluginHookAction
    {
        public string Hook { get; }
        Task Execute(object args);
    }
}