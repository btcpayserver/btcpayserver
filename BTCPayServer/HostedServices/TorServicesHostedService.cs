using System;
using System.Threading.Tasks;
using BTCPayServer.Services;

namespace BTCPayServer.HostedServices
{
    public class TorServicesHostedService : BaseAsyncService
    {
        private readonly TorServices _torServices;

        public TorServicesHostedService(TorServices torServices)
        {
            _torServices = torServices;
        }

        internal override Task[] InitializeTasks()
        {
            return new Task[] { CreateLoopTask(RefreshTorServices) };
        }

        async Task RefreshTorServices()
        {
            await _torServices.Refresh();
            await Task.Delay(TimeSpan.FromSeconds(120), Cancellation);
        }
    }
}
