using System.Threading.Tasks;

namespace BTCPayServer.Abstractions.Contracts
{
    public interface IPluginHookService
    {
        Task ApplyAction(string hook, object args);
        Task<object> ApplyFilter(string hook, object args);
    }
}
