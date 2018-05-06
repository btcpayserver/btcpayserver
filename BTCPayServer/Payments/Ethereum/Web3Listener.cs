using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;

namespace BTCPayServer.Payments.Ethereum
{
    public class Web3Listener : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
