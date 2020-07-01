using System;
using System.Threading.Tasks;
using NBitcoin;
using NBXplorer;
using NBXplorer.Models;

namespace BTCPayServer.Services.Fees
{
    public class NBXplorerFeeProviderFactory : IFeeProviderFactory
    {
        public NBXplorerFeeProviderFactory(ExplorerClientProvider explorerClients)
        {
            if (explorerClients == null)
                throw new ArgumentNullException(nameof(explorerClients));
            _ExplorerClients = explorerClients;
        }

        private readonly ExplorerClientProvider _ExplorerClients;

        public FeeRate Fallback { get; set; }
        public IFeeProvider CreateFeeProvider(BTCPayNetworkBase network)
        {
            return new NBXplorerFeeProvider(this, _ExplorerClients.GetExplorerClient(network));
        }
    }
    public class NBXplorerFeeProvider : IFeeProvider
    {
        public NBXplorerFeeProvider(NBXplorerFeeProviderFactory parent, ExplorerClient explorerClient)
        {
            if (explorerClient == null)
                throw new ArgumentNullException(nameof(explorerClient));
            _Factory = parent;
            _ExplorerClient = explorerClient;
        }

        readonly NBXplorerFeeProviderFactory _Factory;
        readonly ExplorerClient _ExplorerClient;
        public async Task<FeeRate> GetFeeRateAsync(int blockTarget = 20)
        {
            try
            {
                return (await _ExplorerClient.GetFeeRateAsync(blockTarget).ConfigureAwait(false)).FeeRate;
            }
            catch (NBXplorerException ex) when (ex.Error.HttpCode == 400 && ex.Error.Code == "fee-estimation-unavailable")
            {
                return _Factory.Fallback;
            }
        }
    }
}
