using System;
using System.Threading.Tasks;

namespace BTCPayServer.Abstractions.Contracts
{
    public interface IPluginHookService
    {
        Task ApplyAction(string hook, object args);
        Task<object> ApplyFilter(string hook, object args);

        event EventHandler<(string hook, object args)> ActionInvoked;
        event EventHandler<(string hook, object args)> FilterInvoked;
    }
}
