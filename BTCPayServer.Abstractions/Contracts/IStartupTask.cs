using System.Threading;
using System.Threading.Tasks;

namespace BTCPayServer.Abstractions.Contracts
{
    public interface IStartupTask
    {
        Task ExecuteAsync(CancellationToken cancellationToken = default);
    }
}
