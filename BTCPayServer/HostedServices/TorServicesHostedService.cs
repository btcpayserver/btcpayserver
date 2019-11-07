using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Configuration;
using BTCPayServer.Services;
using Microsoft.Extensions.Hosting;

namespace BTCPayServer.HostedServices
{
    public class TorServicesHostedService : BaseAsyncService
    {
        private readonly BTCPayServerOptions _options;
        private readonly TorServices _torServices;

        public TorServicesHostedService(BTCPayServerOptions options, TorServices torServices)
        {
            _options = options;
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
