using System.Threading;
using System.Threading.Tasks;

namespace BTCPayServer.Blazor.VaultBridge;
public interface IController
{
    Task Run(VaultBridgeUI ui, CancellationToken cancellationToken);
}

