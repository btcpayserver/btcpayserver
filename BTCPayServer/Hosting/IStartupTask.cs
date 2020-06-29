using System.Threading;
using System.Threading.Tasks;

namespace BTCPayServer.Hosting
{
    public interface IStartupTask
    {
        Task ExecuteAsync(CancellationToken cancellationToken = default);
    }
}
