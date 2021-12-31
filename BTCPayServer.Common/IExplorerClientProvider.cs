using NBXplorer;

namespace BTCPayServer.Common
{
    public interface IExplorerClientProvider
    {
        ExplorerClient GetExplorerClient(string cryptoCode);
        ExplorerClient GetExplorerClient(BTCPayNetworkBase network);
    }
}
