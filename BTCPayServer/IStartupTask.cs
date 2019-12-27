using System.Threading;
using System.Threading.Tasks;

namespace BTCPayServer
{
    public interface IStartupTask
    {
        Task ExecuteAsync(CancellationToken cancellationToken = default);
    }
}
