using System.Threading.Tasks;
using System.Threading;

namespace BTCPayServer.HostedServices
{
    public interface IPeriodicTask
    {
        Task Do(CancellationToken cancellationToken);
    }
}
